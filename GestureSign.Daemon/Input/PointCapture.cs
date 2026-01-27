using GestureSign.Common;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Log;
using GestureSign.Daemon.Filtration;
using GestureSign.Daemon.Surface;
using GestureSign.PointPatterns;
using ManagedWinapi.Hooks;
using ManagedWinapi.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WindowsInput;

namespace GestureSign.Daemon.Input
{
    public class PointCapture : ILoadable, IPointCapture, IDisposable
    {
        #region Private Variables

        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002; // Don't call back for events on installer's process
        private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;

        // Create new Touch hook control to capture global input from Touch, and create an event translator to get formal events
        private readonly PointEventTranslator _pointEventTranslator;
        private readonly InputProvider _inputProvider;
        private readonly PointerInputTargetWindow _pointerInputTargetWindow;
        private readonly List<PointPattern> _pointPatternCache = new List<PointPattern>();
        private SurfaceForm _surfaceForm;

        private System.Threading.Timer _initialTimeoutTimer;
        private System.Threading.Timer _inactivityTimer;
        private System.Threading.Timer _multiFingerDelayTimer;
        SynchronizationContext _currentContext;

        private Dictionary<int, List<Point>> _pointsCaptured;
        private int _totalFingerCount;
        private HashSet<int> _featureFingerIds;
        private List<InputPoint> _pendingFirstPoints; // Collect fingers during multi-finger delay
        private int _pendingTotalFingerCount; // Total finger count for pending points
        // Create variable to hold the only allowed instance of this class
        static readonly PointCapture _Instance = new PointCapture();

        private CaptureMode _mode = CaptureMode.Normal;
        private volatile CaptureState _state;
        private DateTime _lastInputReceivedTime = DateTime.MinValue; // Track last input for sleep/wake debugging

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        readonly WinEventDelegate _winEventDele;
        private readonly IntPtr _hWinEventHook;
        private GCHandle _winEventGch;

        private bool disposedValue = false; // To detect redundant calls

        private int? _blockTouchInputThreshold;
        private Point _touchPadStartPoint;

        #endregion

        #region PInvoke

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        #endregion

        #region Public Instance Properties

        public Devices SourceDevice { get { return _pointEventTranslator.SourceDevice; } }

        public bool TemporarilyDisableCapture { get; set; }

        public List<Point>[] InputPoints
        {
            get
            {
                if (_pointsCaptured == null)
                    return new List<Point>[0];
                return _pointsCaptured.Values.ToArray();
            }
        }

        public CaptureState State
        {
            get { return _state; }
            set
            {
                if (_state != value)
                {
                    _state = value;
                }
            }
        }

        public CaptureMode Mode
        {
            get { return _mode; }
            set
            {
                if (value == _mode) return;
                _mode = value;
                OnModeChanged(new ModeChangedEventArgs(value));
            }
        }

        #endregion

        #region Custom Events

        public event ApplicationChangedEventHandler ForegroundApplicationsChanged;
        // Create an event to notify subscribers that CaptureState has been changed
        public event ModeChangedEventHandler ModeChanged;

        protected virtual void OnModeChanged(ModeChangedEventArgs e)
        {
            if (ModeChanged != null) ModeChanged(this, e);
        }

        // Create event to notify subscribers that the capture process has started
        public event PointsCapturedEventHandler CaptureStarted;

        protected virtual void OnCaptureStarted(PointsCapturedEventArgs e)
        {
            if (CaptureStarted != null) CaptureStarted(this, e);
        }

        // Create event to notify subscribers that a point set has been captured
        public event PointsCapturedEventHandler AfterPointsCaptured;
        public event PointsCapturedEventHandler BeforePointsCaptured;
        public event RecognitionEventHandler GestureRecognized;
        //public event RecognitionEventHandler GestureNotRecognized;

        protected virtual void OnAfterPointsCaptured(PointsCapturedEventArgs e)
        {
            if (AfterPointsCaptured != null) AfterPointsCaptured(this, e);
        }

        protected virtual void OnBeforePointsCaptured(PointsCapturedEventArgs e)
        {
            if (BeforePointsCaptured != null) BeforePointsCaptured(this, e);
        }

        protected virtual void OnGestureRecognized(RecognitionEventArgs e)
        {
            if (GestureRecognized != null) GestureRecognized(this, e);
        }

        //protected virtual void OnGestureNotRecognized(RecognitionEventArgs e)
        //{
        //    if (GestureNotRecognized != null) GestureNotRecognized(this, e);
        //}

        // Create event to notify subscribers that a single point has been captured
        public event PointsCapturedEventHandler PointCaptured;

        protected virtual void OnPointCaptured(PointsCapturedEventArgs e)
        {
            if (PointCaptured != null) PointCaptured(this, e);
        }

        // Create event to notify subscribers that the capture process has ended
        public event EventHandler CaptureEnded;

        protected virtual void OnCaptureEnded()
        {
            if (CaptureEnded != null) CaptureEnded(this, new EventArgs());
        }

        // Create event to notify subscribers that the capture has been canceled
        public event PointsCapturedEventHandler CaptureCanceled;

        protected virtual void OnCaptureCanceled(PointsCapturedEventArgs e)
        {
            if (CaptureCanceled != null) CaptureCanceled(this, e);
        }

        #endregion

        #region Public Properties

        public static PointCapture Instance
        {
            get { return _Instance; }
        }

        #endregion

        #region Constructors

        protected PointCapture()
        {
            _surfaceForm = new SurfaceForm();

            CaptureStarted += (o, e) => { if (Mode != CaptureMode.UserDisabled) _surfaceForm.StartDrawing(e.FirstCapturedPoints, Mode == CaptureMode.Training, e.FingerCount); };
            CaptureEnded += (o, e) => { _surfaceForm.EndDrawing(); };
            CaptureCanceled += (o, e) => { _surfaceForm.EndDrawing(); };
            PointCaptured += (o, e) =>
            {
                if (Mode != CaptureMode.UserDisabled && State == CaptureState.Capturing)
                {
                    _surfaceForm.DrawPoints(e.Points);
                }
            };

            _inputProvider = new InputProvider();
            _pointEventTranslator = new PointEventTranslator(_inputProvider);
            _pointEventTranslator.PointDown += (PointEventTranslator_PointDown);
            _pointEventTranslator.PointUp += (PointEventTranslator_PointUp);
            _pointEventTranslator.PointMove += (PointEventTranslator_PointMove);

            _currentContext = SynchronizationContext.Current;

            _winEventDele = WinEventProc;
            _winEventGch = GCHandle.Alloc(_winEventDele);
            _hWinEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _winEventDele, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            // Only create PointerInputTargetWindow in UIAccess mode
            // RegisterPointerInputTarget requires UIAccess privilege to work
            if (AppConfig.UiAccess)
            {
                try
                {
                    _pointerInputTargetWindow = new PointerInputTargetWindow();
                    ModeChanged += (o, e) =>
                    {
                        if (e.Mode == CaptureMode.UserDisabled)
                            _pointerInputTargetWindow.BlockTouchInputThreshold = 0;
                    };
                    ForegroundApplicationsChanged += PointCapture_ForegroundApplicationsChanged;

                    GestureSign.Common.Log.Logging.LogInfo("[PointCapture] PointerInputTargetWindow created (UIAccess mode)");
                }
                catch (Exception ex)
                {
                    GestureSign.Common.Log.Logging.LogError($"[PointCapture] Failed to create PointerInputTargetWindow: {ex.Message}");
                    _pointerInputTargetWindow = null;
                }
            }
            else
            {
                GestureSign.Common.Log.Logging.LogInfo("[PointCapture] Skipping PointerInputTargetWindow (non-UIAccess mode)");
            }

            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        }

        #endregion

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _initialTimeoutTimer?.Dispose();
                    _inactivityTimer?.Dispose();
                    _multiFingerDelayTimer?.Dispose();
                    _pointerInputTargetWindow?.Dispose();
                    _inputProvider?.Dispose();
                    _surfaceForm?.Dispose();
                }
                _surfaceForm = null;

                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
                if (_hWinEventHook != IntPtr.Zero)
                    UnhookWinEvent(_hWinEventHook);
                if (_winEventGch.IsAllocated)
                {
                    _winEventGch.Free();
                }

                disposedValue = true;
            }
        }

        ~PointCapture()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region System Events

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND || eventType == EVENT_SYSTEM_MINIMIZEEND)
            {
                if (State != CaptureState.Ready || Mode != CaptureMode.Normal || hwnd.Equals(IntPtr.Zero))
                    return;
                var systemWindow = new SystemWindow(hwnd);
                if (!systemWindow.Visible)
                    return;
                var apps = ApplicationManager.Instance.GetApplicationFromWindow(systemWindow);
                ForegroundApplicationsChanged?.Invoke(this, new ApplicationChangedEventArgs(apps));
            }
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            GestureSign.Common.Log.Logging.LogInfo($"[PointCapture] SessionSwitch event: {e.Reason}, Current State: {State}");

            switch (e.Reason)
            {
                case SessionSwitchReason.RemoteConnect:
                case SessionSwitchReason.SessionLogon:
                case SessionSwitchReason.SessionUnlock:
                    if (State == CaptureState.Disabled)
                    {
                        State = CaptureState.Ready;
                    }
                    break;
                case SessionSwitchReason.SessionLock:
                    State = CaptureState.Disabled;
                    break;
                default:
                    break;
            }
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                var previousState = State;
                Logging.LogInfo($"[PointCapture] PowerMode Resume, State: {previousState}");

                // Reset state machine if stuck in non-Ready state during sleep
                if (previousState != CaptureState.Ready && previousState != CaptureState.Disabled)
                {
                    Logging.LogWarning($"[PointCapture] Resetting stuck state {previousState} → Ready after resume");
                    _pointsCaptured?.Clear();
                    _featureFingerIds?.Clear();
                    _pendingFirstPoints = null;
                    State = CaptureState.Ready;
                }
            }
        }

        #endregion

        #region Events

        private void PointCapture_ForegroundApplicationsChanged(object sender, ApplicationChangedEventArgs appsChanged)
        {
            if (appsChanged.Applications != null)
            {
                var userAppList = appsChanged.Applications.Where(application => application is UserApp).ToList();

                // Calculate threshold: use app-specific or global setting
                int threshold = 0;
                if (userAppList.Count > 0)
                {
                    threshold = userAppList.Cast<UserApp>().Max(app => app.BlockTouchInputThreshold);
                }

                // If BlockWindowsGestures is enabled globally, always use threshold=2 for multi-finger blocking
                if (AppConfig.BlockWindowsGestures && threshold < 2)
                {
                    threshold = 2;
                }

                if (threshold > 0)
                {
                    Logging.LogDebug($"[PointCapture] ForegroundApplicationsChanged: Pre-registering with threshold={threshold}");
                    UpdateBlockTouchInputThreshold(threshold);
                }
            }
        }

        protected void PointEventTranslator_PointDown(object sender, InputPointsEventArgs e)
        {
            // Track input timing for sleep/wake debugging
            var now = DateTime.Now;
            var timeSinceLastInput = now - _lastInputReceivedTime;

            // Log if it's been more than 10 seconds since last input (possible wake from sleep)
            // Skip logging if this is the first input (avoid huge time delta from MinValue)
            if (_lastInputReceivedTime != DateTime.MinValue && timeSinceLastInput.TotalSeconds > 10)
            {
                GestureSign.Common.Log.Logging.LogInfo($"[PointCapture] First input after {timeSinceLastInput.TotalSeconds:F1}s idle - State: {State}, Fingers: {e.TotalFingerCount}");
            }

            _lastInputReceivedTime = now;

            if (State == CaptureState.Ready || State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
            {
                // If already capturing, don't restart - just update total finger count
                // This handles cases where finger count changes mid-gesture (e.g., 2 → 3 → 4 fingers)
                if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
                {
                    _totalFingerCount = Math.Max(_totalFingerCount, e.TotalFingerCount);
                    return;
                }

                // If waiting for multi-finger delay, just add new fingers to pending list
                if (_pendingFirstPoints != null)
                {
                    // Merge new fingers with existing pending fingers
                    foreach (var newPoint in e.InputPointList)
                    {
                        if (!_pendingFirstPoints.Any(p => p.ContactIdentifier == newPoint.ContactIdentifier))
                        {
                            _pendingFirstPoints.Add(newPoint);
                            GestureSign.Common.Log.Logging.LogTrace($"[PointCapture] Added finger during delay: ID={newPoint.ContactIdentifier}, total={_pendingFirstPoints.Count}");
                        }
                    }
                    // Update total finger count to match the current event
                    _pendingTotalFingerCount = Math.Max(_pendingTotalFingerCount, e.TotalFingerCount);
                    return;
                }

                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

                // Start multi-finger delay to collect all fingers for tap gestures
                var multiFingerDelay = AppConfig.MultiFingerDelay;
                if (multiFingerDelay > 0)
                {
                    _pendingFirstPoints = new List<InputPoint>(e.InputPointList);
                    _pendingTotalFingerCount = e.TotalFingerCount;
                    if (_multiFingerDelayTimer == null)
                    {
                        _multiFingerDelayTimer = new System.Threading.Timer(MultiFingerDelayCallback, null, Timeout.Infinite, Timeout.Infinite);
                    }
                    _multiFingerDelayTimer.Change(multiFingerDelay, Timeout.Infinite);
                    GestureSign.Common.Log.Logging.LogTrace($"[PointCapture] Starting multi-finger delay: {multiFingerDelay}ms, fingers={_pendingFirstPoints.Count}");
                    e.Handled = Mode != CaptureMode.UserDisabled;
                    return;
                }

                // No delay configured, start capture immediately
                var timeout = AppConfig.InitialTimeout;
                if (timeout > 0)
                {
                    if (_initialTimeoutTimer == null)
                    {
                        _initialTimeoutTimer = new System.Threading.Timer(InitialTimeoutCallback, null, Timeout.Infinite, Timeout.Infinite);
                    }
                    _initialTimeoutTimer.Change(timeout, Timeout.Infinite);
                }

                // Start inactivity timer to auto-clear stuck gestures (100ms)
                if (_inactivityTimer == null)
                {
                    _inactivityTimer = new System.Threading.Timer(InactivityTimeoutCallback, null, Timeout.Infinite, Timeout.Infinite);
                }
                _inactivityTimer.Change(100, Timeout.Infinite);

                // Try to begin capture process, if capture started then don't notify other applications of a Point event, otherwise do
                if (!TryBeginCapture(e.InputPointList, e.TotalFingerCount))
                {
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                }
                else e.Handled = Mode != CaptureMode.UserDisabled;
            }
        }

        protected void PointEventTranslator_PointMove(object sender, InputPointsEventArgs e)
        {
            // If waiting for multi-finger delay, check if movement is significant enough to start capture
            if (_pendingFirstPoints != null)
            {
                // Calculate maximum movement distance from any finger's initial position
                double maxMovement = 0;
                foreach (var currentPoint in e.InputPointList)
                {
                    var matchingPoints = _pendingFirstPoints.Where(p => p.ContactIdentifier == currentPoint.ContactIdentifier);
                    if (matchingPoints.Any())
                    {
                        var initialPoint = matchingPoints.First();
                        double distance = PointPatternMath.GetDistance(initialPoint.Point, currentPoint.Point);
                        if (distance > maxMovement)
                            maxMovement = distance;
                    }
                }

                // Only start capture if movement exceeds a threshold
                // Use half of TapDistanceThreshold to distinguish intentional swipe from finger jitter
                // This prevents accidental finger jitter from triggering capture for tap gestures
                int threshold = AppConfig.TapDistanceThreshold / 2;
                if (maxMovement >= threshold)
                {
                    _multiFingerDelayTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    StartCaptureAfterDelay();
                    e.Handled = Mode != CaptureMode.UserDisabled;
                    return;
                }
                else
                {
                    // Movement is too small, continue waiting for delay timeout
                    // Update pending points to track latest positions for tap detection
                    foreach (var currentPoint in e.InputPointList)
                    {
                        var idx = _pendingFirstPoints.FindIndex(p => p.ContactIdentifier == currentPoint.ContactIdentifier);
                        if (idx >= 0)
                        {
                            _pendingFirstPoints[idx] = currentPoint;
                        }
                    }
                    e.Handled = Mode != CaptureMode.UserDisabled;
                    return;
                }
            }

            // Only add point if we're capturing
            if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
            {
                AddPoint(e.InputPointList);

                // Reset inactivity timer on each PointMove
                _inactivityTimer?.Change(100, Timeout.Infinite);
            }
            UpdateBlockTouchInputThreshold();
        }

        protected void PointEventTranslator_PointUp(object sender, InputPointsEventArgs e)
        {
            // PointUp = gesture end signal
            // Case 1: During delay (tap gesture) - any finger up ends the gesture
            if (_pendingFirstPoints != null)
            {
                // Cancel delay timer and inactivity timer
                _multiFingerDelayTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _inactivityTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                // Start capture to record finger count, then immediately end
                StartCaptureAfterDelay();
                EndCapture();

                e.Handled = Mode != CaptureMode.UserDisabled;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                return;
            }

            // Case 2: Already capturing - any finger up ends the gesture
            if (State == CaptureState.Capturing || (State == CaptureState.CapturingInvalid && (SourceDevice & Devices.TouchDevice) != 0))
            {
                // Stop inactivity timer
                _inactivityTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                EndCapture();

                if (TemporarilyDisableCapture && Mode == CaptureMode.UserDisabled)
                {
                    TemporarilyDisableCapture = false;
                    ToggleUserDisablePointCapture();
                }

                e.Handled = Mode != CaptureMode.UserDisabled;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                return;
            }
            else if (State == CaptureState.TriggerFired)
            {
                State = CaptureState.Ready;
                e.Handled = Mode != CaptureMode.UserDisabled;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
            }

            UpdateBlockTouchInputThreshold();
            if (_initialTimeoutTimer != null)
                _initialTimeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (_inactivityTimer != null)
            {
                _inactivityTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        #endregion

        #region Private Methods

        private void UpdateBlockTouchInputThreshold(int? threshold = null)
        {
            if (_pointerInputTargetWindow == null) return;

            if (threshold != null)
                _blockTouchInputThreshold = threshold;

            if (_blockTouchInputThreshold.HasValue)
            {
                var thresholdValue = _blockTouchInputThreshold.GetValueOrDefault();
                _blockTouchInputThreshold = null;

                Logging.LogDebug($"[PointCapture] Applying BlockTouchInputThreshold={thresholdValue}");

                // Apply threshold synchronously to ensure blocking takes effect immediately
                // This is critical for preventing the first touch frame from being forwarded to Windows
                _pointerInputTargetWindow.BlockTouchInputThreshold = thresholdValue;
            }
        }

        private void InitialTimeoutCallback(object o)
        {
            _currentContext.Post((state) =>
            {
                if (State != CaptureState.CapturingInvalid) return;

                try
                {
                    if (SourceDevice == Devices.TouchScreen && _pointerInputTargetWindow != null)
                    {
                        if (_pointerInputTargetWindow.BlockTouchInputThreshold > 1)
                            _pointerInputTargetWindow.TemporarilyDisable();
                    }
                    State = CaptureState.Ready;
                }
                catch
                {
                    State = CaptureState.Ready;
                }
            }, null);
        }

        private void InactivityTimeoutCallback(object o)
        {
            _currentContext.Post((state) =>
            {
                // Auto-clear stuck gestures if no PointMove received for 100ms
                if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
                {
                    GestureSign.Common.Log.Logging.LogWarning($"[PointCapture] Inactivity timeout (100ms) - Auto-clearing stuck gesture, State: {State}");

                    // Force end capture to clear the gesture
                    try
                    {
                        // Trigger AfterPointsCaptured event to clear the surface display
                        var emptyArgs = new PointsCapturedEventArgs(new List<Point>());
                        OnAfterPointsCaptured(emptyArgs);

                        State = CaptureState.Ready;
                        _pointsCaptured?.Clear();
                        _featureFingerIds?.Clear();
                        _totalFingerCount = 0;

                        GestureSign.Common.Log.Logging.LogInfo($"[PointCapture] Stuck gesture cleared, State reset to Ready, surface cleared");
                    }
                    catch (Exception ex)
                    {
                        GestureSign.Common.Log.Logging.LogError($"[PointCapture] Error clearing stuck gesture: {ex.Message}");
                    }
                }
            }, null);
        }

        private void MultiFingerDelayCallback(object o)
        {
            _currentContext.Post((state) =>
            {
                StartCaptureAfterDelay();
            }, null);
        }

        private void StartCaptureAfterDelay()
        {
            if (_pendingFirstPoints == null) return;

            var firstPoints = _pendingFirstPoints;
            var totalFingerCount = _pendingTotalFingerCount;
            _pendingFirstPoints = null;
            _pendingTotalFingerCount = 0;

            var timeout = AppConfig.InitialTimeout;
            if (timeout > 0)
            {
                if (_initialTimeoutTimer == null)
                {
                    _initialTimeoutTimer = new System.Threading.Timer(InitialTimeoutCallback, null, Timeout.Infinite, Timeout.Infinite);
                }
                _initialTimeoutTimer.Change(timeout, Timeout.Infinite);
            }

            // Start inactivity timer
            if (_inactivityTimer == null)
            {
                _inactivityTimer = new System.Threading.Timer(InactivityTimeoutCallback, null, Timeout.Infinite, Timeout.Infinite);
            }
            _inactivityTimer.Change(100, Timeout.Infinite);

            // Begin capture with all collected fingers
            if (!TryBeginCapture(firstPoints, totalFingerCount))
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
            }
        }

        private bool TryBeginCapture(List<InputPoint> firstPoint, int totalFingerCount)
        {
            for (int i = 0; i < firstPoint.Count; i++)
            {
                GestureSign.Common.Log.Logging.LogTrace($"  InputPoint[{i}]: ID={firstPoint[i].ContactIdentifier}, Point=({firstPoint[i].Point.X},{firstPoint[i].Point.Y})");
            }

            // Record total finger count for gesture matching
            // Use totalFingerCount parameter instead of firstPoint.Count to get the original finger count
            // (firstPoint may have fewer elements if some fingers have State=None)
            _totalFingerCount = totalFingerCount;

            // Select feature finger based on configuration
            // FeatureFingerIndex: 0-based index (0=leftmost, 1=2nd from left, etc.)
            List<InputPoint> featureFingers;
            var sortedByX = firstPoint.OrderBy(p => p.Point.X).ToList();

            // Get configured feature finger index, bounded by actual finger count
            int configuredIndex = AppConfig.FeatureFingerIndex;
            int actualIndex = Math.Min(configuredIndex, sortedByX.Count - 1);

            featureFingers = new List<InputPoint> { sortedByX[actualIndex] };
            _featureFingerIds = new HashSet<int> { sortedByX[actualIndex].ContactIdentifier };


            // Create capture args so we can notify subscribers that capture has started and allow them to cancel if they want.
            // IMPORTANT: Pass the total finger count, not just feature finger count
            PointsCapturedEventArgs captureStartedArgs;
            if (SourceDevice == Devices.TouchPad)
            {
                _touchPadStartPoint = System.Windows.Forms.Cursor.Position;
                captureStartedArgs = new PointsCapturedEventArgs(featureFingers.Select(p => new List<Point>() { p.Point }).ToList(), new List<Point>() { _touchPadStartPoint });
                captureStartedArgs.FingerCount = _totalFingerCount;
            }
            else
            {
                captureStartedArgs = new PointsCapturedEventArgs(featureFingers.Select(p => p.Point).ToList());
                captureStartedArgs.FingerCount = _totalFingerCount;
            }
            OnCaptureStarted(captureStartedArgs);


            // Determine block threshold: use global setting if enabled, otherwise use app-specific setting
            int blockThreshold = 0;
            if (Mode == CaptureMode.Normal)
            {

                if (AppConfig.BlockWindowsGestures && _totalFingerCount >= 2)
                {
                    // Block all multi-finger gestures to prevent Windows default behavior
                    blockThreshold = 2;
                }
                else
                {
                    blockThreshold = captureStartedArgs.BlockTouchInputThreshold;
                    if (AppConfig.BlockWindowsGestures == false && _totalFingerCount >= 2)
                    {
                    }
                }
            }

            // Logging.LogDebug($"[PointCapture] UpdateBlockTouchInputThreshold: totalFingers={_totalFingerCount}, blockThreshold={blockThreshold}, BlockWindowsGestures={AppConfig.BlockWindowsGestures}, appThreshold={captureStartedArgs.BlockTouchInputThreshold}");
            UpdateBlockTouchInputThreshold(blockThreshold);

            if (captureStartedArgs.Cancel)
            {
                return false;
            }

            // Logging.LogDebug($"[PointCapture] State changed: Ready → CapturingInvalid (fingers={featureFingers.Count})");
            State = CaptureState.CapturingInvalid;

            // Clear old gesture from point list so we can start adding the new captures points to the list
            // Only create entries for feature fingers
            _pointsCaptured = new Dictionary<int, List<Point>>(featureFingers.Count);
            if (AppConfig.IsOrderByLocation)
            {
                foreach (var rawData in featureFingers.OrderBy(p => p.Point.X))
                {
                    if (!_pointsCaptured.ContainsKey(rawData.ContactIdentifier))
                        _pointsCaptured.Add(rawData.ContactIdentifier, new List<Point>(30));
                }
            }
            else
            {
                foreach (var rawData in featureFingers.OrderBy(p => p.ContactIdentifier))
                {
                    if (!_pointsCaptured.ContainsKey(rawData.ContactIdentifier))
                        _pointsCaptured.Add(rawData.ContactIdentifier, new List<Point>(30));
                }
            }
            AddPoint(featureFingers);

            return true;
        }

        private void EndCapture()
        {
            // Check if _pointsCaptured is null (safety check)
            if (_pointsCaptured == null)
            {
                Logging.LogWarning("[PointCapture] EndCapture called but _pointsCaptured is null");
                State = CaptureState.Ready;
                return;
            }

            // Log captured trajectory details
            int trajectoryIndex = 0;
            foreach (var trajectory in _pointsCaptured.Values)
            {
                trajectoryIndex++;
            }

            // Create points capture event args, to be used to send off to event subscribers or to simulate original Point event
            PointsCapturedEventArgs pointsInformation = SourceDevice == Devices.TouchPad ?
                new PointsCapturedEventArgs(_pointsCaptured.Values.ToList(), new List<Point>() { _touchPadStartPoint }) :
                new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values), _pointsCaptured.Values.Select(p => p.FirstOrDefault()).ToList());
            pointsInformation.FingerCount = _totalFingerCount;


            // Notify subscribers that capture has ended （draw end）
            OnCaptureEnded();
            State = CaptureState.Ready;

            // Notify PointsCaptured event subscribers that points have been captured.
            //CaptureWindow GetGestureName
            OnBeforePointsCaptured(pointsInformation);


            if (pointsInformation.Cancel) return;

            if (Mode == CaptureMode.Training)
            {
                // Send gesture to ControlPanel, including tap gestures (even with only 1 point)
                // Only skip if there are no points at all
                if (_pointsCaptured.Count > 0 && _pointsCaptured.Values.Any(v => v.Count > 0))
                {
                    _pointPatternCache.Clear();
                    var pointPattern = new PointPattern(_pointsCaptured.Values, _totalFingerCount);
                    _pointPatternCache.Add(pointPattern);

                    if (!NamedPipe.SendMessageAsync(IpcCommands.GotGesture, Constants.ControlPanel, _pointPatternCache.ToArray(), false).Result)
                        Mode = CaptureMode.Normal;
                }
            }
            else
            {
            }

            // Fire recognized event if we found a gesture match, otherwise throw not recognized event
            if (GestureManager.Instance.GestureName != null)
            {
                List<Point> capturedPoints = SourceDevice == Devices.TouchPad ? new List<Point>() { _touchPadStartPoint } : pointsInformation.FirstCapturedPoints;
                OnGestureRecognized(new RecognitionEventArgs(GestureManager.Instance.GestureName, pointsInformation.Points, capturedPoints, _pointsCaptured.Keys.ToList()));
            }
            //else
            //    OnGestureNotRecognized(new RecognitionEventArgs(pointsInformation.Points, pointsInformation.FirstCapturedPoints, _pointsCaptured.Keys.ToList()));

            OnAfterPointsCaptured(pointsInformation);

            _pointsCaptured.Clear();
        }

        //private void CancelCapture(int num)
        //{
        //    // Notify subscribers that gesture capture has been canceled
        //    OnCaptureCanceled(new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values)));
        //}

        private void AddPoint(List<InputPoint> point)
        {
            bool getNewPoint = false;
            int threshold = AppConfig.MinimumPointDistance;

            foreach (var p in point)
            {

                // Don't accept point if it's within specified distance of last point unless it's the first point
                if (_pointsCaptured.TryGetValue(p.ContactIdentifier, out List<Point> stroke))
                {

                    if (stroke.Count != 0)
                    {
                        double distance = PointPatternMath.GetDistance(stroke.Last(), p.Point);

                        if (PointPatternMath.GetDistance(stroke.Last(), p.Point) < threshold)
                        {
                            continue;
                        }

                        if (State == CaptureState.CapturingInvalid)
                        {
                            // Logging.LogDebug($"[PointCapture] State changed: CapturingInvalid → Capturing (distance={distance:F1}, threshold={threshold:F1})");
                            State = CaptureState.Capturing;
                        }
                    }

                    getNewPoint = true;
                    // Add point to captured points list
                    stroke.Add(p.Point);
                }
                else
                {
                }
            }
            if (getNewPoint)
            {
                // Notify subscribers that point has been captured
                var args = new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values), point.Select(p => p.Point).ToList())
                {
                    FingerCount = _totalFingerCount
                };
                OnPointCaptured(args);
            }
        }



        #endregion

        #region Public Methods

        public void Load()
        {
            // Shortcut method to control singleton instantiation
        }

        public void ToggleUserDisablePointCapture()
        {
            // Toggle User selected Gesture Disabling
            // Added UserDisabled to CaptureState enum since Ready and Disabled can't be used
            // due to the existing logic of Enabling/Disabling for UI/menu popup/etc.
            // The reason I had to set state to Ready if !UserDisabled was due to the sequence of the tray events.
            // I originally had to set to Disable since if you're in the popup it's disabled, however, the popup onclose
            // fires before the menu item's code, so it was back to Ready before this block was executed.  Although, it probably
            // makes more sense to set it to Ready in the event this is called from another location.
            Mode = Mode == CaptureMode.UserDisabled ? CaptureMode.Normal : CaptureMode.UserDisabled;
        }

        #endregion
    }
}
