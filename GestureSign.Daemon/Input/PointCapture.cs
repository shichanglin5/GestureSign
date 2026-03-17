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
        private readonly IContactGestureRecognizer _multiFingerTapRecognizer = new MultiFingerTapRecognizer();
        private readonly TipTapRecognizer _tipTapRecognizer = new TipTapRecognizer();
        private readonly List<PointPattern> _pointPatternCache = new List<PointPattern>();
        private SurfaceForm _surfaceForm;

        private System.Threading.Timer _initialTimeoutTimer;
        private System.Threading.Timer _inactivityTimer;
        SynchronizationContext _currentContext;

        private Dictionary<int, List<Point>> _pointsCaptured;
        private Dictionary<int, List<Point>> _allPointsCaptured;
        private readonly List<TrainingDiagnosticFrame> _trainingDiagnosticFrames = new List<TrainingDiagnosticFrame>();
        private HashSet<int> _activeContactIds;
        private readonly List<int> _contactDownOrder = new List<int>();
        private readonly List<int> _contactUpOrder = new List<int>();
        private readonly Dictionary<int, double> _contactDownTimesMs = new Dictionary<int, double>();
        private readonly Dictionary<int, double> _contactUpTimesMs = new Dictionary<int, double>();
        private bool _isPrimaryButtonDown;
        private bool _primaryButtonWasPressed;
        private int _primaryButtonFingerCount;
        private double? _primaryButtonDownTimeMs;
        private double? _primaryButtonUpTimeMs;
        /// <summary>
        /// 按钮按下瞬间各手指的位置快照，用于计算按压期间的位移。
        /// </summary>
        private Dictionary<int, Point> _primaryButtonDownPositions;
        /// <summary>
        /// 当前会话中观察到的最大同时手指数，在 TrackAllPoints 中通过 _activeContactIds.Count 追踪
        /// </summary>
        private int _peakFingerCount;
        private HashSet<int> _featureFingerIds;
        /// <summary>
        /// 每个手指最后一次明显移动的 session 时间（绝对 session 时间）。
        /// 用于 TipTap fix 手指「最近静止段」判定：tapDownMs - max(lastMovedMs, subSessionStartMs) >= FixMinHoldMs。
        /// </summary>
        private readonly Dictionary<int, double> _contactLastMovedTimeMs = new Dictionary<int, double>();
        /// <summary>
        /// 每个手指在当前子 session 中的起始时间。RebaseSubSession 时重置为当前 session 时间。
        /// 作为 _contactLastMovedTimeMs 的下界，防止 rebase 前的移动时间影响新子 session 的 fix hold 判定。
        /// </summary>
        private readonly Dictionary<int, double> _contactSubSessionStartMs = new Dictionary<int, double>();
        /// <summary>
        /// 每个手指上一帧的位置，用于检测手指是否移动超过抖动阈值。
        /// </summary>
        private readonly Dictionary<int, Point> _contactLastPosition = new Dictionary<int, Point>();
        private static double FingerMovementJitterThresholdPx => AppConfig.FingerMovementJitterThresholdPx;
        /// <summary>
        /// rebase 后是否有新的有效输入（PointDown 或有效 PointMove）。
        /// 为 false 时 EndCapture 跳过终端分类，避免 rebase 残余 session 产生无意义的手势。
        /// </summary>
        private bool _hasNewInputSinceLastRebase = true;
        // Create variable to hold the only allowed instance of this class
        static readonly PointCapture _Instance = new PointCapture();

        private CaptureMode _mode = CaptureMode.Normal;
        private volatile CaptureState _state;
        private DateTime _lastInputReceivedTime = DateTime.MinValue; // Track last input for sleep/wake debugging
        private DateTime _contactSessionStartedAt = DateTime.MinValue;
        private DateTime _trainingDiagnosticStartedAt = DateTime.MinValue;

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        readonly WinEventDelegate _winEventDele;
        private readonly IntPtr _hWinEventHook;
        private GCHandle _winEventGch;

        private bool disposedValue = false; // To detect redundant calls
        private readonly bool _isTestMode;
        private readonly Devices _sourceDeviceOverride;
        private readonly List<RecordedGestureDefinitionResult> _publishedTrainingDefinitionsForTest = new List<RecordedGestureDefinitionResult>();

        private int? _blockTouchInputThreshold;
        private Point _touchPadStartPoint;
        private Point _touchPadOriginPoint;  // 触控板手势的原始起点，用于坐标转换
        private bool _touchPadReferenceInitialized;
        private const int BaseInactivityTimeoutMs = 100;
        private const int MultiFingerInactivityTimeoutMs = 240;
        private static bool EnableTipTapRecognition => true;

        /// <summary>
        /// 特征手指绘制：锁定后的 contactId，null 表示尚未锁定。
        /// 锁定条件（满足其一）：任一手指移动超过阈值，或首手指 down 超过时间阈值。
        /// </summary>
        private int? _featureFingerDrawContactId;

        #endregion

        #region PInvoke

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        #endregion

        #region Public Instance Properties

        public Devices SourceDevice { get { return _isTestMode ? _sourceDeviceOverride : _pointEventTranslator.SourceDevice; } }

        internal IReadOnlyList<RecordedGestureDefinitionResult> PublishedTrainingDefinitionsForTest => _publishedTrainingDefinitionsForTest;

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

                if (value == CaptureMode.Training || _mode == CaptureMode.Training)
                {
                    ResetSessionTracking();
                    if (State != CaptureState.Disabled)
                    {
                        State = CaptureState.Ready;
                    }
                }

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
            : this(false, Devices.None)
        {
        }

        internal PointCapture(Devices sourceDeviceForTest)
            : this(true, sourceDeviceForTest)
        {
        }

        private PointCapture(bool isTestMode, Devices sourceDeviceOverride)
        {
            _isTestMode = isTestMode;
            _sourceDeviceOverride = sourceDeviceOverride;

            if (!_isTestMode)
            {
                _surfaceForm = new SurfaceForm();
            }

            // Must be set AFTER SurfaceForm creation: creating a Form installs
            // WindowsFormsSynchronizationContext.  Without it, timer callbacks
            // run on the ThreadPool instead of the UI thread, breaking gesture capture.
            _currentContext = SynchronizationContext.Current ?? new SynchronizationContext();

            CaptureStarted += (o, e) => { if (_surfaceForm != null && Mode != CaptureMode.UserDisabled) _surfaceForm.StartDrawing(e.FirstCapturedPoints, Mode == CaptureMode.Training, e.FingerCount); };
            CaptureEnded += (o, e) => { _surfaceForm?.EndDrawing(); };
            CaptureCanceled += (o, e) => { _surfaceForm?.EndDrawing(); };
            PointCaptured += (o, e) =>
            {
                // 允许 CapturingInvalid 状态下也绘制轨迹，避免手势轨迹不显示的问题
                if (_surfaceForm != null && Mode != CaptureMode.UserDisabled && (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid))
                {
                    // 特征手指绘制模式：只绘制锁定的特征手指轨迹
                    List<List<Point>> drawPoints = e.Points;
                    if (AppConfig.DrawFeatureFingerOnly && _featureFingerDrawContactId.HasValue
                        && _pointsCaptured != null && _pointsCaptured.TryGetValue(_featureFingerDrawContactId.Value, out var featureStroke))
                    {
                        drawPoints = new List<List<Point>> { new List<Point>(featureStroke) };
                    }
                    else if (AppConfig.DrawFeatureFingerOnly && !_featureFingerDrawContactId.HasValue)
                    {
                        // 尚未锁定特征手指，不绘制
                        return;
                    }

                    // 触摸板 _pointsCaptured 存原始坐标，绘制时需偏移到光标位置
                    if (SourceDevice == Devices.TouchPad && _touchPadReferenceInitialized)
                    {
                        int offsetX = _touchPadStartPoint.X - _touchPadOriginPoint.X;
                        int offsetY = _touchPadStartPoint.Y - _touchPadOriginPoint.Y;
                        var translated = drawPoints.Select(stroke =>
                            stroke.Select(p => new Point(p.X + offsetX, p.Y + offsetY)).ToList()
                        ).ToList();
                        _surfaceForm.DrawPoints(translated);
                    }
                    else
                    {
                        _surfaceForm.DrawPoints(drawPoints);
                    }
                }
            };

            if (_isTestMode)
            {
                _inputProvider = null;
                _pointEventTranslator = null;
                _pointerInputTargetWindow = null;
                _winEventDele = null;
                _hWinEventHook = IntPtr.Zero;
                return;
            }

            _inputProvider = new InputProvider();
            _pointEventTranslator = new PointEventTranslator(_inputProvider);
            _pointEventTranslator.PointDown += (PointEventTranslator_PointDown);
            _pointEventTranslator.PointUp += (PointEventTranslator_PointUp);
            _pointEventTranslator.PointMove += (PointEventTranslator_PointMove);

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
                }
                catch (Exception ex)
                {
                    GestureSign.Common.Log.Logging.LogError($"[PointCapture] Failed to create PointerInputTargetWindow: {ex.Message}");
                    _pointerInputTargetWindow = null;
                }
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
                {
                    return;
                }
                var systemWindow = new SystemWindow(hwnd);
                if (!systemWindow.Visible)
                    return;
                var apps = ApplicationManager.Instance.GetApplicationFromWindow(systemWindow);
                ForegroundApplicationsChanged?.Invoke(this, new ApplicationChangedEventArgs(apps));
            }
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            GestureSign.Common.Log.Logging.LogDebug($"[PointCapture] SessionSwitch event: {e.Reason}, Current State: {State}");

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
                    // 锁屏时注销 PointerInputTargetWindow，避免唤醒后残留注册导致阻止所有触摸输入
                    ResetPointerInputTarget();
                    State = CaptureState.Disabled;
                    break;
                default:
                    break;
            }

            GestureSign.Common.Log.Logging.LogDebug($"[PointCapture] SessionSwitch event: {e.Reason}, After State: {State}");
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                var previousState = State;
                Logging.LogInfo($"[PointCapture] PowerMode Resume, State: {previousState}");

                // 唤醒时注销 PointerInputTargetWindow，避免残留注册导致阻止所有触摸输入
                ResetPointerInputTarget();

                // Reset state machine if stuck in non-Ready state during sleep
                if (previousState != CaptureState.Ready && previousState != CaptureState.Disabled)
                {
                    Logging.LogWarning($"[PointCapture] Resetting stuck state {previousState} → Ready after resume");
                    ResetSessionTracking();
                    State = CaptureState.Ready;
                }
            }
            else if (e.Mode == PowerModes.Suspend)
            {
                // 睡眠时注销 PointerInputTargetWindow
                ResetPointerInputTarget();
            }
        }

        private void ResetPointerInputTarget()
        {
            if (_pointerInputTargetWindow != null)
            {
                Logging.LogDebug("[PointCapture] Resetting PointerInputTargetWindow registration");
                _pointerInputTargetWindow.BlockTouchInputThreshold = 0;
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

                // Logging.LogDebug($"[PointCapture] ForegroundApplicationsChanged: threshold={threshold}, BlockWindowsGestures={AppConfig.BlockWindowsGestures}, userAppCount={userAppList.Count}");
                if (threshold > 0)
                {
                    UpdateBlockTouchInputThreshold(threshold);
                }
            }
        }

        protected void PointEventTranslator_PointDown(object sender, InputPointsEventArgs e)
        {
            // Training and runtime must share the same session boundary behavior.
            // If a previous contact session already ended but its metadata has not been
            // cleared yet, the next real contact must start a fresh session before any
            // new diagnostics or trajectory data are recorded.
            if (ShouldResetSessionBeforePointDown(
                HasTrackedContactSessionData(),
                _activeContactIds != null && _activeContactIds.Count > 0,
                State,
                e?.InputPointList))
            {
                ResetSessionTracking();
            }

            RecordTrainingDiagnosticFrame("down", e);

            // Track input timing for sleep/wake debugging
            var now = DateTime.Now;
            var timeSinceLastInput = now - _lastInputReceivedTime;

            // Log if it's been more than 10 seconds since last input (possible wake from sleep)
            // Skip logging if this is the first input (avoid huge time delta from MinValue)
            if (_lastInputReceivedTime != DateTime.MinValue && timeSinceLastInput.TotalSeconds > 10)
            {
                // GestureSign.Common.Log.Logging.LogDebug($"[PointCapture] First input after {timeSinceLastInput.TotalSeconds:F1}s idle - State: {State}, Fingers: {e.TotalFingerCount}");
            }

            _lastInputReceivedTime = now;

            if (State == CaptureState.Ready || State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
            {
                TrackAllPoints(e.InputPointList);

                // If already capturing, don't restart — _peakFingerCount is tracked in TrackAllPoints
                if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
                {
                    _hasNewInputSinceLastRebase = true;
                    var oldPeak = _peakFingerCount;
                    if (_activeContactIds != null && _activeContactIds.Count > _peakFingerCount)
                    {
                        _peakFingerCount = _activeContactIds.Count;
                    }

                    if (_peakFingerCount > oldPeak)
                    {
                        _surfaceForm?.UpdateFingerCount(_peakFingerCount);
                    }

                    e.Handled = Mode != CaptureMode.UserDisabled;
                    return;
                }

                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

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
                _inactivityTimer.Change(GetInactivityTimeoutMs(), Timeout.Infinite);

                if (!TryBeginCapture(e.InputPointList))
                {
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                }
                else e.Handled = Mode != CaptureMode.UserDisabled;
            }
        }

        protected void PointEventTranslator_PointMove(object sender, InputPointsEventArgs e)
        {
            RecordTrainingDiagnosticFrame("move", e);

            TrackAllPoints(e.InputPointList);

            // Only add point if we're capturing
            if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
            {
                AddPoint(e.InputPointList);

                // Reset inactivity timer on each PointMove
                _inactivityTimer?.Change(GetInactivityTimeoutMs(), Timeout.Infinite);
            }
            UpdateBlockTouchInputThreshold();
        }

        protected void PointEventTranslator_PointUp(object sender, InputPointsEventArgs e)
        {
            RecordTrainingDiagnosticFrame("up", e);

            // TrackAllPoints 内部会调用 InferImplicitContactReleases（触控板专用）。
            TrackAllPoints(e.InputPointList);

            // 触控屏需要额外的隐式释放推断，逻辑与触控板不同，在 PointUp 事件中单独处理。
            InferTouchScreenImplicitReleases(e.InputPointList);

            bool allContactsReleased = AreAllTrackedContactsReleased();

            // Wait for all fingers to release before ending capture
            if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
            {
                if (!allContactsReleased)
                {
                    // ── 阶段 1: 实时 TipTap 检测（部分手指释放）──
                    if (EnableTipTapRecognition && TryRecognizeTipTapOnPartialRelease(e))
                    {
                        e.Handled = Mode != CaptureMode.UserDisabled;
                        return;
                    }

                    // 部分手指先抬起时保持会话，避免超时把多指手势静默清理。
                    _inactivityTimer?.Change(GetInactivityTimeoutMs(), Timeout.Infinite);
                    e.Handled = Mode != CaptureMode.UserDisabled;
                    return;
                }

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
            UpdateBlockTouchInputThreshold();
            if (_initialTimeoutTimer != null)
                _initialTimeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (_inactivityTimer != null)
            {
                _inactivityTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
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

                // Logging.LogDebug($"[PointCapture] Applying BlockTouchInputThreshold={thresholdValue} state={State} fingers={_peakFingerCount}");

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
                // Auto-clear stuck gestures if no PointMove received for the timeout period.
                // Only clear if no contacts are active: active contacts mean fingers are still on screen,
                // in which case we just reset the timer and wait (touches may have paused mid-gesture).
                if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
                {
                    int activeContacts = _activeContactIds?.Count ?? 0;

                    if (activeContacts > 0)
                    {
                        // Fingers still on screen — not a stuck gesture, just a pause. Keep waiting.
                        _inactivityTimer?.Change(GetInactivityTimeoutMs(), Timeout.Infinite);
                        return;
                    }

                    int trajectories = _pointsCaptured?.Count ?? 0;
                    int totalPoints = _pointsCaptured?.Values.Sum(v => v.Count) ?? 0;
                    GestureSign.Common.Log.Logging.LogWarning($"[PointCapture] Inactivity timeout ({GetInactivityTimeoutMs()}ms) - clearing stuck capture, state={State}, peakFingers={_peakFingerCount}, trajectories={trajectories}, totalPoints={totalPoints}");

                    // Force end capture to clear the gesture
                    try
                    {
                        // Trigger AfterPointsCaptured event to clear the surface display
                        var emptyArgs = new PointsCapturedEventArgs(new List<Point>());
                        OnAfterPointsCaptured(emptyArgs);

                        State = CaptureState.Ready;
                        ResetSessionTracking();
                    }
                    catch (Exception ex)
                    {
                        GestureSign.Common.Log.Logging.LogError($"[PointCapture] Error clearing stuck gesture: {ex.Message}");
                    }
                }
            }, null);
        }

        internal static int GetInactivityTimeoutMs(int peakFingerCount)
        {
            return peakFingerCount >= 4 ? MultiFingerInactivityTimeoutMs : BaseInactivityTimeoutMs;
        }

        private int GetInactivityTimeoutMs()
        {
            return GetInactivityTimeoutMs(_peakFingerCount);
        }

        private bool TryBeginCapture(List<InputPoint> firstPoint)
        {
            int activeFingerCount = _activeContactIds?.Count ?? firstPoint.Count(p => p.State != DeviceStates.None);
            if (activeFingerCount == 0)
                return false;
            if (Mode == CaptureMode.Training && activeFingerCount < 2)
            {
                return false;
            }

            _peakFingerCount = activeFingerCount;
            // 保留 TrackAllPoints 已收集的轨迹数据（手指可能在 TryBeginCapture 之前就已 down）
            _allPointsCaptured ??= new Dictionary<int, List<Point>>(firstPoint.Count);
            _activeContactIds ??= new HashSet<int>();
            foreach (var p in firstPoint.Where(p => p.State != DeviceStates.None))
                _activeContactIds.Add(p.ContactIdentifier);

            // 所有活跃手指都作为特征手指，录制和匹配使用全部手指的轨迹
            var sortedByX = firstPoint.OrderBy(p => p.Point.X).ToList();
            List<InputPoint> featureFingers = sortedByX.Where(p => p.State != DeviceStates.None).ToList();
            _featureFingerIds = new HashSet<int>(featureFingers.Select(p => p.ContactIdentifier));

            // Create capture args so we can notify subscribers that capture has started and allow them to cancel if they want.
            // IMPORTANT: Pass the total finger count, not just feature finger count
            PointsCapturedEventArgs captureStartedArgs;
            if (SourceDevice == Devices.TouchPad)
            {
                EnsureTouchPadReferenceInitialized(firstPoint, activeFingerCount);
                captureStartedArgs = new PointsCapturedEventArgs(featureFingers.Select(p => new List<Point>() { p.Point }).ToList(), new List<Point>() { _touchPadStartPoint });
                captureStartedArgs.FingerCount = _peakFingerCount;
            }
            else
            {
                captureStartedArgs = new PointsCapturedEventArgs(featureFingers.Select(p => p.Point).ToList());
                captureStartedArgs.FingerCount = _peakFingerCount;
            }
            captureStartedArgs.AllPoints = firstPoint.Select(p => new List<Point> { TranslateTouchPadPoint(p.Point) }).ToList();
            captureStartedArgs.Session = CreateSessionSnapshot();
            OnCaptureStarted(captureStartedArgs);


            // Determine block threshold: use global setting if enabled, otherwise use app-specific setting
            int blockThreshold = 0;
            if (Mode == CaptureMode.Normal)
            {

                if (AppConfig.BlockWindowsGestures && _peakFingerCount >= 2)
                {
                    // Block all multi-finger gestures to prevent Windows default behavior
                    blockThreshold = 2;
                }
                else
                {
                    blockThreshold = captureStartedArgs.BlockTouchInputThreshold;
                    if (AppConfig.BlockWindowsGestures == false && _peakFingerCount >= 2)
                    {
                    }
                }
            }

            // Logging.LogDebug($"[PointCapture] TryBeginCapture: totalFingers={_peakFingerCount}, blockThreshold={blockThreshold}, BlockWindowsGestures={AppConfig.BlockWindowsGestures}, appThreshold={captureStartedArgs.BlockTouchInputThreshold}, Cancel={captureStartedArgs.Cancel}");
            // 单指时不改变 PointerInputTargetWindow 注册状态，避免破坏预注册
            // 预注册保持有效时，单指触摸经过截获→注入的完整路径，PointerID 一致，应用能正常处理
            if (_peakFingerCount >= 2)
            {
                UpdateBlockTouchInputThreshold(blockThreshold);
            }

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
            // rebase 后无新输入直接释放时，跳过终端分类，只清理状态
            if (!_hasNewInputSinceLastRebase)
            {
                OnCaptureEnded();
                State = CaptureState.Ready;
                ResetSessionTracking();
                return;
            }

            // Check if _pointsCaptured is null (safety check)
            if (_pointsCaptured == null)
            {
                Logging.LogWarning("[PointCapture] EndCapture called but _pointsCaptured is null");
                State = CaptureState.Ready;
                return;
            }

            // Create points capture event args, to be used to send off to event subscribers or to simulate original Point event
            PointsCapturedEventArgs pointsInformation = SourceDevice == Devices.TouchPad ?
                new PointsCapturedEventArgs(_pointsCaptured.Values.ToList(), new List<Point>() { _touchPadStartPoint }) :
                new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values), _pointsCaptured.Values.Select(p => p.FirstOrDefault()).ToList());
            pointsInformation.FingerCount = _peakFingerCount;
            pointsInformation.AllPoints = _allPointsCaptured?.Values.ToList() ?? new List<List<Point>>();
            pointsInformation.Session = CreateSessionSnapshot();
            double analysisDurationMs = GestureSessionTiming.GetEffectiveGestureDurationMs(pointsInformation.Session);
            pointsInformation.Analysis = GestureAnalyzer.Analyze(pointsInformation.AllPoints, _peakFingerCount,
                analysisDurationMs);

            // 触摸板单指滑动不需要走手势识别流程（无轨迹手势/contact gesture 可匹配）
            if (SourceDevice == Devices.TouchPad && _peakFingerCount <= 1 && Mode != CaptureMode.Training)
            {
                OnCaptureEnded();
                State = CaptureState.Ready;
                OnAfterPointsCaptured(pointsInformation);
                ResetSessionTracking();
                return;
            }

            var contactGesture = TryRecognizeContactGesture(pointsInformation.Session, pointsInformation.Analysis);
            if (contactGesture.IsMatch)
            {
                Logging.LogTrace($"[PointCapture] Contact gesture recognized in session: kind={contactGesture.Kind}, variant={contactGesture.Variant}, fingers={contactGesture.FingerCount}");
            }


            // Notify subscribers that capture has ended （draw end）
            OnCaptureEnded();
            State = CaptureState.Ready;

            // Notify PointsCaptured event subscribers that points have been captured.
            OnBeforePointsCaptured(pointsInformation);

            // 采样当前修饰符状态（结束时松开的修饰键不计入）
            var activeModifiers = GetCurrentModifiers();

            // Run trajectory gesture recognition and store result for use below.
            var trajectoryPoints = pointsInformation.Points.Select(l => l.ToArray()).ToArray();
            var trajectoryMatch = GestureManager.Instance.Recognize(trajectoryPoints, pointsInformation.FingerCount, activeModifiers);


            if (pointsInformation.Cancel)
            {
                ResetSessionTracking();
                return;
            }

            if (Mode == CaptureMode.Training)
            {
                if (_pointsCaptured.Count > 0 && _pointsCaptured.Values.Any(v => v.Count > 0))
                {
                    _pointPatternCache.Clear();
                    var pointPattern = new PointPattern(_pointsCaptured.Values, _peakFingerCount);
                    _pointPatternCache.Add(pointPattern);

                    var trajectoryGesture = new Gesture(null, _pointPatternCache.ToArray(), _peakFingerCount);
                    trajectoryGesture.Modifiers = activeModifiers;
                    var existingSimilarGestureName = GestureManager.Instance.GetMostSimilarGestureName(_pointPatternCache.ToArray(), activeModifiers);
                    if (!string.IsNullOrEmpty(existingSimilarGestureName))
                    {
                        var existingGesture = GestureManager.Instance.GetNewestGestureSample(existingSimilarGestureName) as Gesture;
                        if (existingGesture != null)
                        {
                            trajectoryGesture.Id = existingGesture.Id;
                            trajectoryGesture.Name = existingGesture.Name;
                        }
                    }

                    var sample = new RecordedGestureSample
                    {
                        FingerCount = _peakFingerCount,
                        DurationMs = analysisDurationMs,
                        Analysis = pointsInformation.Analysis,
                        Session = pointsInformation.Session,
                        AllPoints = pointsInformation.AllPoints,
                        FeatureTrajectories = _pointsCaptured?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    };

                    var definition = GestureDefinitionFactory.Create(sample, trajectoryGesture, activeModifiers);
                    definition.DiagnosticData = TrainingDiagnosticsFormatter.Format(sample, definition, _trainingDiagnosticFrames, SourceDevice);

                    if (_isTestMode)
                    {
                        _publishedTrainingDefinitionsForTest.Add(definition);
                    }
                    else
                    {
                        NamedPipe.SendMessageAsync(IpcCommands.GotGesture, Constants.ControlPanel, definition, false)
                            .ContinueWith(t =>
                            {
                                if (!t.Result)
                                    _currentContext?.Post(_ => { Mode = CaptureMode.Normal; }, null);
                            }, TaskContinuationOptions.OnlyOnRanToCompletion);
                    }
                }

                ResetSessionTracking();
                return;
            }

            if (contactGesture.IsMatch && contactGesture.Kind == ContactGestureKind.MultiFingerClick)
            {
                // Click = Tap + PrimaryButtonDown，统一到 Tap 匹配路径
                var clickAsTapModifiers = activeModifiers | GestureModifiers.PrimaryButtonDown;
                var tapFromClickDefinition = ApplicationManager.Instance.GetRecognizedTapDefinitions(contactGesture.FingerCount, clickAsTapModifiers).FirstOrDefault();
                if (tapFromClickDefinition != null)
                {
                    FireContactGestureRecognized(pointsInformation, tapFromClickDefinition.Id, tapFromClickDefinition.Name);
                    ResetSessionTracking();
                    return;
                }
            }

            if (contactGesture.IsMatch && contactGesture.Kind == ContactGestureKind.MultiFingerTap)
            {
                var tapDefinition = ApplicationManager.Instance.GetRecognizedTapDefinitions(contactGesture.FingerCount, activeModifiers).FirstOrDefault();
                if (tapDefinition != null)
                {
                    FireContactGestureRecognized(pointsInformation, tapDefinition.Id, tapDefinition.Name);
                    ResetSessionTracking();
                    return;
                }
            }

            if (EnableTipTapRecognition)
            {
                var tipTapConfigs = ApplicationManager.Instance.GetRecognizedTipTapConfigs(pointsInformation.FingerCount, activeModifiers).ToList();
                foreach (var tipTapConfig in tipTapConfigs)
                {
                    var effectiveConfig = new TipTapGestureConfig
                    {
                        Id = tipTapConfig.Id,
                        Name = tipTapConfig.Name,
                        IsEnabled = tipTapConfig.IsEnabled,
                        FingerCount = tipTapConfig.FingerCount,
                        FixFingerCount = tipTapConfig.FixFingerCount,
                        Direction = tipTapConfig.Direction,
                        Modifiers = tipTapConfig.Modifiers,
                        Recognition = GetEffectiveTipTapRecognition(tipTapConfig.Recognition),
                    };
                    var tipTapResult = _tipTapRecognizer.ProcessSnapshot(pointsInformation.Session, effectiveConfig);
                    if (!tipTapResult.IsMatch)
                        continue;

                    Logging.LogTrace($"[PointCapture] TipTap recognized in session: variant={tipTapResult.Variant}, fingers={tipTapResult.FingerCount}");

                    FireContactGestureRecognized(pointsInformation, tipTapConfig.Id, tipTapConfig.Name);
                    ResetSessionTracking();
                    return;
                }
            }

            // Fire recognized event if we found a gesture match, otherwise throw not recognized event
            int trajCount = _pointsCaptured?.Count ?? 0;
            int totalPoints = _pointsCaptured?.Values.Sum(v => v.Count) ?? 0;
            var matchStrategy = AppConfig.TrajectoryMatchStrategy;
            if (trajectoryMatch != null)
            {
                // Logging.LogTrace($"[PointCapture] Trajectory gesture matched: name={trajectoryMatch.Name}, trajectories={trajCount}, totalPoints={totalPoints}, fingers={_peakFingerCount}, strategy={matchStrategy}");
                List<Point> capturedPoints = SourceDevice == Devices.TouchPad ? [_touchPadStartPoint] : pointsInformation.FirstCapturedPoints;
                OnGestureRecognized(new RecognitionEventArgs(trajectoryMatch.Id, trajectoryMatch.Name, pointsInformation.Points, capturedPoints, [.. _pointsCaptured.Keys]));
            }
            // else
            // {
            //     Logging.LogTrace($"[PointCapture] No trajectory gesture matched: trajectories={trajCount}, totalPoints={totalPoints}, fingers={_peakFingerCount}, strategy={matchStrategy}");
            // }
            OnAfterPointsCaptured(pointsInformation);
            ResetSessionTracking();
        }

        //private void CancelCapture(int num)
        //{
        //    // Notify subscribers that gesture capture has been canceled
        //    OnCaptureCanceled(new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values)));
        //}

        /// <summary>
        /// 采样当前激活的手势修饰符状态（EndCapture 时或连续手势首帧时调用）。
        /// 结束时已松开的修饰键不计入。
        /// </summary>
        public GestureModifiers GetCurrentModifiers()
        {
            var m = GestureModifiers.Default;
            if (_primaryButtonWasPressed) m |= GestureModifiers.PrimaryButtonDown;
            var modKeys = System.Windows.Forms.Control.ModifierKeys;
            if ((modKeys & System.Windows.Forms.Keys.Control) != 0) m |= GestureModifiers.Ctrl;
            if ((modKeys & System.Windows.Forms.Keys.Shift) != 0) m |= GestureModifiers.Shift;
            if ((modKeys & System.Windows.Forms.Keys.Alt) != 0) m |= GestureModifiers.Alt;
            return m;
        }

        /// <summary>
        /// 将触控板坐标转换为屏幕坐标（使用全局参考点，仅用于参考手指自身）
        /// </summary>
        private Point TranslateTouchPadPoint(Point touchPadPoint)
        {
            if (SourceDevice != Devices.TouchPad)
                return touchPadPoint;

            return new Point(
                _touchPadStartPoint.X + (touchPadPoint.X - _touchPadOriginPoint.X),
                _touchPadStartPoint.Y + (touchPadPoint.Y - _touchPadOriginPoint.Y)
            );
        }


        internal static bool TryGetTouchPadReferencePoint(IReadOnlyList<InputPoint> points, int totalFingerCount, out InputPoint referencePoint)
        {
            referencePoint = default;
            if (points == null)
                return false;

            var activePoints = points
                .Where(point => point.State != DeviceStates.None)
                .OrderBy(point => point.Point.X)
                .ToList();
            if (activePoints.Count == 0)
                return false;

            int effectiveFingerCount = totalFingerCount > 0 ? totalFingerCount : activePoints.Count;
            int configuredIndex = effectiveFingerCount <= 2 ? 0 : 1;
            int actualIndex = Math.Min(configuredIndex, activePoints.Count - 1);
            referencePoint = activePoints[actualIndex];
            return true;
        }

        private void EnsureTouchPadReferenceInitialized(IReadOnlyList<InputPoint> points, int totalFingerCount)
        {
            if (SourceDevice != Devices.TouchPad || _touchPadReferenceInitialized)
                return;

            if (!TryGetTouchPadReferencePoint(points, totalFingerCount, out var referencePoint))
                return;

            _touchPadStartPoint = System.Windows.Forms.Cursor.Position;
            _touchPadOriginPoint = referencePoint.Point;
            _touchPadReferenceInitialized = true;
        }

        private void ResetSessionTracking()
        {
            _pointsCaptured?.Clear();
            _allPointsCaptured?.Clear();
            _trainingDiagnosticFrames.Clear();
            _featureFingerIds?.Clear();
            _activeContactIds?.Clear();
            _contactDownOrder.Clear();
            _contactUpOrder.Clear();
            _contactDownTimesMs.Clear();
            _contactUpTimesMs.Clear();
            _isPrimaryButtonDown = false;
            _primaryButtonWasPressed = false;
            _primaryButtonFingerCount = 0;
            _primaryButtonDownTimeMs = null;
            _primaryButtonUpTimeMs = null;
            _primaryButtonDownPositions = null;
            _contactLastMovedTimeMs.Clear();
            _contactSubSessionStartMs.Clear();
            _contactLastPosition.Clear();
            _tipTapRecognizer.Reset();
            _featureFingerDrawContactId = null;
            _peakFingerCount = 0;
            _touchPadStartPoint = Point.Empty;
            _touchPadOriginPoint = Point.Empty;
            _touchPadReferenceInitialized = false;
            _contactSessionStartedAt = DateTime.MinValue;
            _trainingDiagnosticStartedAt = DateTime.MinValue;
            _hasNewInputSinceLastRebase = true;
        }

        /// <summary>
        /// 统一子 session 重置：TipTap 触发后和 Click 触发后共用。
        /// 通知外部 CaptureEnded（重置连续手势状态），清除已触发手指的数据，
        /// 重置活跃手指的轨迹和子 session 时间。
        /// 关键设计：不重置 fix 手指的 _contactDownTimesMs（保持原始 down 时间），
        /// 使得 rebase 后 fix 手指直接抬起时 session duration 很长 → Tap 因超过 MaxDurationMs 自然失败。
        /// </summary>
        private void RebaseSubSession(int? triggeredContactId = null)
        {
            // 1. 通知外部：CaptureEnded → ContinuousGestureTrigger 重置滚动状态
            OnCaptureEnded();

            // 2. 如果有特定触发手指（TipTap 的 tap 手指），清除其数据
            if (triggeredContactId.HasValue)
            {
                int tapId = triggeredContactId.Value;
                _contactDownTimesMs.Remove(tapId);
                _contactUpTimesMs.Remove(tapId);
                _contactDownOrder.Remove(tapId);
                _contactUpOrder.Remove(tapId);
                _allPointsCaptured?.Remove(tapId);
                _contactLastMovedTimeMs.Remove(tapId);
                _contactSubSessionStartMs.Remove(tapId);
                _contactLastPosition.Remove(tapId);
            }

            // 3. 重置活跃手指的轨迹和子 session 时间
            var now = GetContactSessionElapsedMs();
            if (_activeContactIds != null)
            {
                foreach (int contactId in _activeContactIds)
                {
                    if (_allPointsCaptured != null && _allPointsCaptured.TryGetValue(contactId, out var traj) && traj != null && traj.Count > 0)
                    {
                        var lastPoint = traj[traj.Count - 1];
                        traj.Clear();
                        traj.Add(lastPoint);
                    }
                    _contactUpTimesMs.Remove(contactId);
                    _contactUpOrder.Remove(contactId);
                    _contactSubSessionStartMs[contactId] = now;
                    // rebase 后 fix 手指视为"从很早就静止"，
                    // 设为 0 使 effectiveLastMoved 由实际移动事件决定而非 subSessionStart
                    _contactLastMovedTimeMs.Remove(contactId);
                    // 注意：不重置 _contactDownTimesMs[contactId]（保持原始 down 时间，用于 Tap duration）
                }
            }

            // 4. 重置 session 级状态
            _pointsCaptured?.Clear();
            _featureFingerIds?.Clear();
            _featureFingerDrawContactId = null;
            _peakFingerCount = _activeContactIds?.Count ?? 0;
            // 子 session rebase：按键若仍处于按下状态则保留 WasPressed，下一次 TipTap 仍能正确带 PrimaryButtonDown 修饰符
            _primaryButtonWasPressed = _isPrimaryButtonDown;
            _isPrimaryButtonDown = false;  // 下一帧 TrackPrimaryButton 会根据实际帧状态重建
            _primaryButtonDownTimeMs = null;
            _primaryButtonUpTimeMs = null;
            _primaryButtonFingerCount = 0;
            _primaryButtonDownPositions = null;
            _trainingDiagnosticFrames.Clear();
            _trainingDiagnosticStartedAt = DateTime.MinValue;

            // 5. 触摸板参考点 rebase
            // （保留 _touchPadReferenceInitialized 和 _touchPadOriginPoint，下一帧 TrackAllPoints 时自然更新）

            // 6. 标记 rebase 后尚无新输入
            _hasNewInputSinceLastRebase = false;

            // 7. State = CapturingInvalid（等待新手势开始）
            State = CaptureState.CapturingInvalid;
        }

        internal static bool ShouldResetSessionBeforePointDown(
            bool hasTrackedSessionData,
            bool hasActiveContacts,
            CaptureState state,
            IReadOnlyList<InputPoint> points)
        {
            if (!hasTrackedSessionData || hasActiveContacts)
                return false;

            if (state == CaptureState.Capturing || state == CaptureState.CapturingInvalid)
                return false;

            // This is a shared guard for both runtime and training. The caller may later
            // record extra diagnostics in training mode, but the new physical contact must
            // always begin from a clean logical session.
            return points != null && points.Any(point => point.State != DeviceStates.None);
        }

        private bool HasTrackedContactSessionData()
        {
            return (_allPointsCaptured != null && _allPointsCaptured.Count > 0)
                || _contactDownOrder.Count > 0
                || _contactUpOrder.Count > 0
                || _contactDownTimesMs.Count > 0
                || _contactUpTimesMs.Count > 0
                || _primaryButtonWasPressed
                || _trainingDiagnosticFrames.Count > 0;
        }

        private void RecordTrainingDiagnosticFrame(string eventType, InputPointsEventArgs e)
        {
            if (Mode != CaptureMode.Training || e?.InputPointList == null || e.InputPointList.Count == 0)
                return;

            if (_trainingDiagnosticStartedAt == DateTime.MinValue)
                _trainingDiagnosticStartedAt = DateTime.UtcNow;

            var frame = new TrainingDiagnosticFrame
            {
                EventType = eventType,
                ElapsedMs = (DateTime.UtcNow - _trainingDiagnosticStartedAt).TotalMilliseconds,
                TotalFingerCount = e.TotalFingerCount,
            };

            foreach (var inputPoint in e.InputPointList)
            {
                var translatedPoint = TranslateTouchPadPoint(inputPoint.Point);
                frame.Points.Add(new TrainingDiagnosticPoint
                {
                    ContactId = inputPoint.ContactIdentifier,
                    State = inputPoint.State,
                    RawX = inputPoint.Point.X,
                    RawY = inputPoint.Point.Y,
                    TranslatedX = translatedPoint.X,
                    TranslatedY = translatedPoint.Y,
                });
            }

            _trainingDiagnosticFrames.Add(frame);
        }

        private void EnsureContactSessionStarted()
        {
            if (_contactSessionStartedAt == DateTime.MinValue)
                _contactSessionStartedAt = DateTime.UtcNow;
        }

        private double GetContactSessionElapsedMs()
        {
            return _contactSessionStartedAt == DateTime.MinValue
                ? 0
                : (DateTime.UtcNow - _contactSessionStartedAt).TotalMilliseconds;
        }

        private void PublishTrainingGestureDefinition(PointsCapturedEventArgs pointsInformation, TipTapGestureConfig matchedTipTapConfig = null, GestureModifiers? overrideModifiers = null)
        {
            if (Mode != CaptureMode.Training || pointsInformation == null)
                return;

            var sample = new RecordedGestureSample
            {
                FingerCount = _peakFingerCount,
                DurationMs = GestureSessionTiming.GetEffectiveGestureDurationMs(pointsInformation.Session),
                Analysis = pointsInformation.Analysis,
                Session = pointsInformation.Session,
                AllPoints = pointsInformation.AllPoints,
            };

            var definition = matchedTipTapConfig == null
                ? GestureDefinitionFactory.Create(sample, null, overrideModifiers ?? GetCurrentModifiers())
                : GestureDefinitionFactory.CreateTipTapMatch(sample, matchedTipTapConfig);
            definition.DiagnosticData = TrainingDiagnosticsFormatter.Format(sample, definition, _trainingDiagnosticFrames, SourceDevice);

            if (_isTestMode)
            {
                _publishedTrainingDefinitionsForTest.Add(definition);
                return;
            }

            NamedPipe.SendMessageAsync(IpcCommands.GotGesture, Constants.ControlPanel, definition, false)
                .ContinueWith(t =>
                {
                    if (!t.Result)
                        _currentContext?.Post(_ => { Mode = CaptureMode.Normal; }, null);
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        internal void ProcessPointDownForTest(List<InputPoint> points, int totalFingerCount)
        {
            PointEventTranslator_PointDown(this, new InputPointsEventArgs(points, SourceDevice, totalFingerCount));
        }

        internal void ProcessPointMoveForTest(List<InputPoint> points, int totalFingerCount)
        {
            PointEventTranslator_PointMove(this, new InputPointsEventArgs(points, SourceDevice, totalFingerCount));
        }

        internal void ProcessPointUpForTest(List<InputPoint> points, int totalFingerCount)
        {
            PointEventTranslator_PointUp(this, new InputPointsEventArgs(points, SourceDevice, totalFingerCount));
        }

        /// <summary>
        /// 实时 Click 检测：在 TrackPrimaryButton 中物理按键释放且手指仍在板上时调用。
        /// </summary>
        private bool TryRecognizeClickImmediate()
        {
            var snapshot = CreateSessionSnapshot();
            var allPoints = _allPointsCaptured?.Values.ToList() ?? new List<List<Point>>();
            double analysisDurationMs = GestureSessionTiming.GetEffectiveGestureDurationMs(snapshot);
            var analysis = GestureAnalyzer.Analyze(allPoints, _peakFingerCount, analysisDurationMs);

            int fingerCount = snapshot?.PrimaryButtonFingerCount > 0
                ? snapshot.PrimaryButtonFingerCount
                : snapshot?.FingerCount > 0 ? snapshot.FingerCount : analysis.FingerCount;
            var effectiveClickRecognition = GetEffectiveClickRecognition(null, fingerCount);
            double pressDisplacement = GetPrimaryButtonPressDisplacement();
            if (!MultiFingerClickRecognizer.IsMatch(snapshot, pressDisplacement, effectiveClickRecognition))
                return false;

            Logging.LogTrace($"[PointCapture] Real-time Click recognized: fingers={fingerCount}, pressDisplacement={pressDisplacement:F1}");

            var pointsInfo = SourceDevice == Devices.TouchPad
                ? new PointsCapturedEventArgs(allPoints, new List<Point> { _touchPadStartPoint })
                : new PointsCapturedEventArgs(allPoints, allPoints.Select(p => p.FirstOrDefault()).ToList());
            pointsInfo.FingerCount = _peakFingerCount;
            pointsInfo.AllPoints = allPoints;
            pointsInfo.Session = snapshot;
            pointsInfo.Analysis = analysis;

            ApplicationManager.Instance.GetForegroundApplications();
            // Click = Tap + PrimaryButtonDown，统一到 Tap 匹配路径
            var clickModifiers = GetCurrentModifiers() | GestureModifiers.PrimaryButtonDown;
            var tapFromClickDef = ApplicationManager.Instance.GetRecognizedTapDefinitions(fingerCount, clickModifiers).FirstOrDefault();

            if (Mode == CaptureMode.Training)
            {
                // Click 训练需传入 clickModifiers（含 PrimaryButtonDown），
                // 此时物理按键已松开，GetCurrentModifiers() 不含 PrimaryButtonDown
                PublishTrainingGestureDefinition(pointsInfo, overrideModifiers: clickModifiers);
                OnAfterPointsCaptured(pointsInfo);
            }
            else if (tapFromClickDef != null)
            {
                FireContactGestureRecognized(pointsInfo, tapFromClickDef.Id, tapFromClickDef.Name);
            }
            else
            {
                Logging.LogTrace($"[PointCapture] Real-time Click recognized but no Tap+PrimaryButtonDown definition found: fingers={fingerCount}");
                return false;
            }

            RebaseSubSession(triggeredContactId: null);
            return true;
        }

        /// <summary>
        /// 实时 TipTap 检测：在 PointUp 的 !allReleased 分支调用。
        /// 检查刚释放的手指是否为 tap 手指，仍在板上的手指是否为静止的 fix 手指。
        /// </summary>
        private bool TryRecognizeTipTapOnPartialRelease(InputPointsEventArgs e)
        {
            // Step 4 实现
            if (!EnableTipTapRecognition || _activeContactIds == null || _activeContactIds.Count < 1)
                return false;

            // 找到刚释放的手指 ID（已被 TrackAllPoints 从 _activeContactIds 移除）
            var releasedIds = e.InputPointList
                .Where(p => p.State == DeviceStates.None && !_activeContactIds.Contains(p.ContactIdentifier))
                .Select(p => p.ContactIdentifier)
                .Where(id => _contactUpTimesMs.ContainsKey(id))
                .ToList();

            if (releasedIds.Count == 0)
                return false;

            ApplicationManager.Instance.GetForegroundApplications();

            foreach (int candidateTapId in releasedIds)
            {
                var candidateFixIds = _activeContactIds.ToList();
                if (candidateFixIds.Count < 1)
                    continue;

                var configs = ApplicationManager.Instance.GetRecognizedTipTapConfigsByFixCount(candidateFixIds.Count, GetCurrentModifiers()).ToList();
                foreach (var config in configs)
                {
                    if (!config.IsEnabled)
                        continue;

                    var effectiveRecognition = GetEffectiveTipTapRecognition(config.Recognition);

                    // 检查 fix 手指最近是否静止
                    if (!IsFixFingersRecentlyStill(candidateFixIds, candidateTapId, effectiveRecognition))
                        continue;

                    // 构建裁剪后的 snapshot（fix 轨迹从子 session 开始后的部分）
                    var snapshot = CreateTrimmedSnapshotForTipTap(candidateTapId, candidateFixIds);
                    if (snapshot == null)
                        continue;

                    var result = TipTapRecognizer.TryMatchWithKnownRoles(
                        snapshot, candidateTapId, candidateFixIds,
                        effectiveRecognition, config.Direction,
                        out var match);

                    if (!result)
                        continue;

                    Logging.LogTrace($"[PointCapture] Real-time TipTap recognized: tap={candidateTapId}, fix=[{string.Join(",", candidateFixIds)}], direction={match.Direction}");

                    // 构建事件参数
                    var allPoints = _allPointsCaptured?.Values.ToList() ?? new List<List<Point>>();
                    var pointsInfo = SourceDevice == Devices.TouchPad
                        ? new PointsCapturedEventArgs(allPoints, new List<Point> { _touchPadStartPoint })
                        : new PointsCapturedEventArgs(allPoints, allPoints.Select(p => p.FirstOrDefault()).ToList());
                    pointsInfo.FingerCount = _peakFingerCount;
                    pointsInfo.AllPoints = allPoints;
                    pointsInfo.Session = snapshot;
                    pointsInfo.Analysis = GestureAnalyzer.Analyze(allPoints, _peakFingerCount,
                        GestureSessionTiming.GetEffectiveGestureDurationMs(snapshot));

                    if (Mode == CaptureMode.Training)
                    {
                        PublishTrainingGestureDefinition(pointsInfo, config);
                        OnAfterPointsCaptured(pointsInfo);
                    }
                    else
                    {
                        FireContactGestureRecognized(pointsInfo, config.Id, config.Name);
                    }

                    RebaseSubSession(triggeredContactId: candidateTapId);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查 fix 手指在 tap 手指 down 前是否保持静止足够时间。
        /// 使用 _contactLastMovedTimeMs 和 _contactSubSessionStartMs 计算最近静止段。
        /// </summary>
        private bool IsFixFingersRecentlyStill(IReadOnlyList<int> fixIds, int tapId, TipTapRecognition recognition)
        {
            if (fixIds == null || fixIds.Count == 0 || recognition == null)
                return false;

            if (!_contactDownTimesMs.TryGetValue(tapId, out double tapDownMs))
                return false;

            foreach (int fixId in fixIds)
            {
                // _contactLastMovedTimeMs 记录手指最后一次实际移动（超过抖动阈值）的时间。
                // 如果手指从未移动过（key 不存在），视为"一直静止"→ 直接通过。
                if (!_contactLastMovedTimeMs.TryGetValue(fixId, out double fixLastMoved))
                    continue;

                if (tapDownMs - fixLastMoved < recognition.FixMinHoldMs)
                {
                    Logging.LogTrace($"[PointCapture] Fix finger {fixId} not still enough: tapDown={tapDownMs:F0}ms, lastMoved={fixLastMoved:F0}ms, required={recognition.FixMinHoldMs}ms");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 创建裁剪后的 snapshot，用于 TipTap 实时检测。
        /// fix 手指的轨迹裁剪为"最后静止段"，确保 GetTrajectoryDistance 只看停止移动后的位移。
        /// </summary>
        private GestureSessionSnapshot CreateTrimmedSnapshotForTipTap(int tapId, IReadOnlyList<int> fixIds)
        {
            var baseSnapshot = CreateSessionSnapshot();
            if (baseSnapshot == null)
                return null;

            var trimmedTrajectories = new Dictionary<int, List<Point>>(baseSnapshot.ContactTrajectories);

            // 确保 ContactUpOrder 中 tap 排在 fix 前面
            var normalizedUpOrder = new List<int>();
            bool tapReleased = baseSnapshot.ContactUpOrder?.Contains(tapId) ?? false;
            if (tapReleased)
                normalizedUpOrder.Add(tapId);
            if (baseSnapshot.ContactUpOrder != null)
                normalizedUpOrder.AddRange(baseSnapshot.ContactUpOrder.Where(id => id != tapId && fixIds.Contains(id)));

            // ContactDownOrder: fix 在前 tap 在后
            var normalizedDownOrder = new List<int>(fixIds);
            if (!normalizedDownOrder.Contains(tapId))
                normalizedDownOrder.Add(tapId);

            var cycleIds = new HashSet<int>(fixIds) { tapId };

            return new GestureSessionSnapshot
            {
                FingerCount = fixIds.Count + 1,
                DurationMs = baseSnapshot.DurationMs,
                ActiveContactIds = baseSnapshot.ActiveContactIds?.Where(cycleIds.Contains).ToList() ?? new List<int>(),
                ContactDownOrder = normalizedDownOrder,
                ContactUpOrder = normalizedUpOrder,
                ContactDownTimesMs = baseSnapshot.ContactDownTimesMs?
                    .Where(kvp => cycleIds.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<int, double>(),
                ContactUpTimesMs = baseSnapshot.ContactUpTimesMs?
                    .Where(kvp => cycleIds.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<int, double>(),
                ContactTrajectories = trimmedTrajectories
                    .Where(kvp => cycleIds.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                AllPoints = trimmedTrajectories
                    .Where(kvp => cycleIds.Contains(kvp.Key))
                    .Select(kvp => kvp.Value)
                    .ToList(),
            };
        }

        private ContactGestureResult TryRecognizeContactGesture(GestureSessionSnapshot session, GestureAnalysis analysis)
        {
            int fc = session?.FingerCount > 0 ? session.FingerCount : analysis.FingerCount;
            var effectiveClickRecognition = GetEffectiveClickRecognition(null, fc);
            if (MultiFingerClickRecognizer.IsMatch(session, analysis?.MaxPerFingerDistance ?? 0, effectiveClickRecognition))
            {
                int clickFingers = session?.PrimaryButtonFingerCount > 0 ? session.PrimaryButtonFingerCount : fc;
                return new ContactGestureResult(true, ContactGestureKind.MultiFingerClick, clickFingers, $"{clickFingers}-finger-click");
            }

            return _multiFingerTapRecognizer.TryRecognize(session, analysis);
        }

        /// <summary>
        /// 从 AppConfig 构建运行时 TipTapRecognition，忽略存储值。
        /// </summary>
        private static TipTapRecognition GetEffectiveTipTapRecognition(TipTapRecognition stored)
        {
            stored ??= new TipTapRecognition();
            return new TipTapRecognition
            {
                MaxTapDurationMs = AppConfig.TipTapMaxTapDurationMs,
                FixMinHoldMs = AppConfig.TipTapFixMinHoldMs,
                FixStillThresholdPx = AppConfig.TapDistanceThreshold,
                TapMaxMovementPx = AppConfig.TapDistanceThreshold,
                DirectionDeadzonePx = stored.DirectionDeadzonePx,
                RepeatCooldownMs = stored.RepeatCooldownMs,
            };
        }

        /// <summary>
        /// 从 AppConfig 构建运行时 ClickGestureRecognition，
        /// 并根据手指数量增加位移容错（每多一指 +5px）。
        /// </summary>
        private static ClickGestureRecognition GetEffectiveClickRecognition(ClickGestureRecognition stored, int fingerCount)
        {
            stored ??= new ClickGestureRecognition();
            double baseMovement = AppConfig.ClickMaxMovementPx;
            double extraPerFinger = fingerCount > 2 ? (fingerCount - 2) * 5.0 : 0;
            return new ClickGestureRecognition
            {
                MaxPressDurationMs = AppConfig.ClickMaxPressDurationMs,
                MaxMovementPx = baseMovement + extraPerFinger,
                MinFingerCount = stored.MinFingerCount,
            };
        }

        private bool AreAllTrackedContactsReleased()
        {
            return _activeContactIds == null || _activeContactIds.Count == 0;
        }

        private void TrackAllPoints(List<InputPoint> points)
        {
            if (points == null || points.Count == 0)
                return;

            EnsureContactSessionStarted();
            // 确保触摸板参考点在首次记录轨迹数据前初始化，
            // 避免第一帧使用 raw 坐标而后续帧使用 translated 坐标导致轨迹跳变
            EnsureTouchPadReferenceInitialized(points, points.Count(p => p.State != DeviceStates.None));
            TrackPrimaryButton(points);
            _allPointsCaptured ??= new Dictionary<int, List<Point>>(points.Count);
            _activeContactIds ??= new HashSet<int>();

            foreach (var inputPoint in points)
            {
                if (!_allPointsCaptured.TryGetValue(inputPoint.ContactIdentifier, out var stroke))
                {
                    stroke = new List<Point>(30);
                    _allPointsCaptured[inputPoint.ContactIdentifier] = stroke;
                }

                if (inputPoint.State != DeviceStates.None)
                {
                    bool isNewlyActiveContact = !_activeContactIds.Contains(inputPoint.ContactIdentifier);

                    // 如果此 ContactId 之前已经 up 过，又重新 down，则覆盖 timing
                    // 实现 incarnation 跟踪：up 清除旧记录，down 写入新记录
                    if (!_contactDownTimesMs.ContainsKey(inputPoint.ContactIdentifier)
                        || (_contactUpTimesMs.ContainsKey(inputPoint.ContactIdentifier) && isNewlyActiveContact))
                    {
                        // 清除旧 up 记录，允许新 incarnation
                        _contactUpTimesMs.Remove(inputPoint.ContactIdentifier);
                        _contactUpOrder.Remove(inputPoint.ContactIdentifier);
                        _contactDownTimesMs[inputPoint.ContactIdentifier] = GetContactSessionElapsedMs();
                        if (!_contactDownOrder.Contains(inputPoint.ContactIdentifier))
                        {
                            _contactDownOrder.Add(inputPoint.ContactIdentifier);
                        }
                        // 重置轨迹为新 incarnation
                        stroke.Clear();
                    }

                    _activeContactIds.Add(inputPoint.ContactIdentifier);

                    Point actualPoint = TranslateTouchPadPoint(inputPoint.Point);
                    if (stroke.Count == 0 || PointPatternMath.GetDistance(stroke[stroke.Count - 1], actualPoint) >= GetContactTrackingThreshold())
                    {
                        stroke.Add(actualPoint);
                    }

                    // 追踪手指移动时间，用于 TipTap fix 手指静止判定。
                    // 使用原始设备坐标（inputPoint.Point）而非屏幕转换后坐标，
                    // 避免 TouchPad 坐标转换因鼠标位置变化而产生虚假位移。
                    var rawPoint = inputPoint.Point;
                    if (_contactLastPosition.TryGetValue(inputPoint.ContactIdentifier, out var lastPos))
                    {
                        double moveDist = PointPatternMath.GetDistance(lastPos, rawPoint);
                        if (moveDist > FingerMovementJitterThresholdPx)
                        {
                            _contactLastMovedTimeMs[inputPoint.ContactIdentifier] = GetContactSessionElapsedMs();
                            _contactLastPosition[inputPoint.ContactIdentifier] = rawPoint;
                        }
                    }
                    else
                    {
                        _contactLastPosition[inputPoint.ContactIdentifier] = rawPoint;
                    }
                }
                else
                {
                    _activeContactIds.Remove(inputPoint.ContactIdentifier);
                    if (!_contactUpTimesMs.ContainsKey(inputPoint.ContactIdentifier))
                    {
                        _contactUpTimesMs[inputPoint.ContactIdentifier] = GetContactSessionElapsedMs();
                        _contactUpOrder.Add(inputPoint.ContactIdentifier);
                    }
                }
            }

            InferImplicitContactReleases(points);
        }

        /// <summary>
        /// 触控板隐式释放推断：在每帧 TrackAllPoints 末尾调用。
        /// 触控板驱动在每帧都会报告所有活跃触点（含静止触点），
        /// 因此某个已跟踪 contactId 从帧中缺席即意味着它已被驱动静默释放。
        /// 注意：触控屏不能使用此逻辑，因为触控屏静止手指不发 UPDATE，
        /// 缺席不等于释放——触控屏专用逻辑见 InferTouchScreenImplicitReleases。
        /// </summary>
        private void InferImplicitContactReleases(List<InputPoint> points)
        {
            if (SourceDevice != Devices.TouchPad || _activeContactIds == null || _activeContactIds.Count == 0)
                return;

            var reportedIds = new HashSet<int>(points.Select(point => point.ContactIdentifier));
            var missingIds = _activeContactIds.Where(id => !reportedIds.Contains(id)).ToList();
            if (missingIds.Count == 0)
                return;

            foreach (int missingId in missingIds)
            {
                _activeContactIds.Remove(missingId);
                if (!_contactUpTimesMs.ContainsKey(missingId))
                {
                    _contactUpTimesMs[missingId] = GetContactSessionElapsedMs();
                    _contactUpOrder.Add(missingId);
                }
            }
        }

        /// <summary>
        /// 触控屏隐式释放推断：仅在 PointUp 事件中调用（不在 PointMove 中调用）。
        /// 触控屏驱动只在触点状态变化时上报，静止触点不发 UPDATE，
        /// 因此不能像触控板那样用"帧缺席"推断释放（会误判静止手指）。
        /// 但在 PointUp 帧中：驱动会报告所有发生状态变化的触点（UP 或仍接触）。
        /// 若某已跟踪 contactId 在 PointUp 帧中完全缺席（既非 UP 也非接触），
        /// 说明驱动在此帧丢失了该触点的 UP 事件——可安全推断为同步释放。
        /// </summary>
        private void InferTouchScreenImplicitReleases(List<InputPoint> points)
        {
            // 仅对触控屏生效，且当前帧必须包含至少一个 UP 事件（否则不是 PointUp 帧）
            if (SourceDevice != Devices.TouchScreen || _activeContactIds == null || _activeContactIds.Count == 0)
                return;
            if (!points.Any(p => p.State == DeviceStates.None))
                return;

            var reportedIds = new HashSet<int>(points.Select(p => p.ContactIdentifier));
            var implicitReleases = _activeContactIds.Where(id => !reportedIds.Contains(id)).ToList();
            if (implicitReleases.Count == 0)
                return;

            foreach (int missingId in implicitReleases)
            {
                Logging.LogDebug($"[PointCapture] TouchScreen implicit release: contactId={missingId} absent from PointUp frame (driver dropped UP event)");
                _activeContactIds.Remove(missingId);
                if (!_contactUpTimesMs.ContainsKey(missingId))
                {
                    _contactUpTimesMs[missingId] = GetContactSessionElapsedMs();
                    _contactUpOrder.Add(missingId);
                }
            }
        }

        private static double GetContactTrackingThreshold()
        {
            return Math.Max(6, Math.Min(AppConfig.MinimumPointDistance / 2.0, AppConfig.TapDistanceThreshold / 4.0));
        }

        private GestureSessionSnapshot CreateSessionSnapshot()
        {
            return new GestureSessionSnapshot
            {
                FingerCount = _peakFingerCount,
                DurationMs = GetContactSessionElapsedMs(),
                ActiveContactIds = _activeContactIds?.ToList() ?? new List<int>(),
                ContactDownOrder = new List<int>(_contactDownOrder),
                ContactUpOrder = new List<int>(_contactUpOrder),
                ContactDownTimesMs = new Dictionary<int, double>(_contactDownTimesMs),
                ContactUpTimesMs = new Dictionary<int, double>(_contactUpTimesMs),
                HasPrimaryButtonClick = _primaryButtonWasPressed,
                PrimaryButtonFingerCount = _primaryButtonFingerCount,
                PrimaryButtonDownTimeMs = _primaryButtonDownTimeMs,
                PrimaryButtonUpTimeMs = _primaryButtonUpTimeMs ?? (_isPrimaryButtonDown && _primaryButtonWasPressed ? GetContactSessionElapsedMs() : null),
                ContactTrajectories = _allPointsCaptured?.ToDictionary(kvp => kvp.Key, kvp => new List<Point>(kvp.Value)) ?? new Dictionary<int, List<Point>>(),
                AllPoints = _allPointsCaptured?.Values.Select(v => new List<Point>(v)).ToList() ?? new List<List<Point>>()
            };
        }

        private void TrackPrimaryButton(IReadOnlyCollection<InputPoint> points)
        {
            bool hasPrimaryButton = points.Any(point => (point.State & DeviceStates.PrimaryButton) == DeviceStates.PrimaryButton);
            if (hasPrimaryButton && !_isPrimaryButtonDown)
            {
                _isPrimaryButtonDown = true;
                _primaryButtonWasPressed = true;
                _primaryButtonDownTimeMs ??= GetContactSessionElapsedMs();
                _primaryButtonFingerCount = Math.Max(_primaryButtonFingerCount, GetPrimaryButtonFingerCount(points));

                // 记录按钮按下时各手指位置，用于计算按压期间位移
                _primaryButtonDownPositions = SnapshotActiveContactPositions();
            }
            else if (!hasPrimaryButton && _isPrimaryButtonDown)
            {
                _isPrimaryButtonDown = false;
                _primaryButtonUpTimeMs ??= GetContactSessionElapsedMs();

                // 实时 Click 检测：物理按键释放且手指仍在板上
                if (_activeContactIds != null && _activeContactIds.Count > 0)
                {
                    TryRecognizeClickImmediate();
                }
            }
        }

        private int GetPrimaryButtonFingerCount(IReadOnlyCollection<InputPoint> points)
        {
            int activePoints = points.Count(point => point.State != DeviceStates.None);
            int activeContactCount = _activeContactIds?.Count ?? 0;
            return new[] { activePoints, activeContactCount, _peakFingerCount }.Max();
        }

        /// <summary>
        /// 快照当前活跃手指的最新位置（来自 _allPointsCaptured 各轨迹的最后一个点）。
        /// </summary>
        private Dictionary<int, Point> SnapshotActiveContactPositions()
        {
            var snapshot = new Dictionary<int, Point>();
            if (_activeContactIds == null || _allPointsCaptured == null)
                return snapshot;

            foreach (int id in _activeContactIds)
            {
                if (_allPointsCaptured.TryGetValue(id, out var traj) && traj.Count > 0)
                    snapshot[id] = traj[traj.Count - 1];
            }
            return snapshot;
        }

        /// <summary>
        /// 计算按钮按下期间各手指的最大位移：按下时位置 vs 当前（释放时）位置的最大距离。
        /// </summary>
        private double GetPrimaryButtonPressDisplacement()
        {
            if (_primaryButtonDownPositions == null || _primaryButtonDownPositions.Count == 0 || _allPointsCaptured == null)
                return 0;

            double maxDisplacement = 0;
            foreach (var kvp in _primaryButtonDownPositions)
            {
                if (_allPointsCaptured.TryGetValue(kvp.Key, out var traj) && traj.Count > 0)
                {
                    double dist = PointPatternMath.GetDistance(kvp.Value, traj[traj.Count - 1]);
                    if (dist > maxDisplacement)
                        maxDisplacement = dist;
                }
            }
            return maxDisplacement;
        }

        /// <summary>
        /// 通过 Action 系统执行 contact gesture（Tap/Click/TipTap）的命令。
        /// 使用 gestureId 匹配 Action.GestureId，走标准的 OnGestureRecognized 路径。
        /// </summary>
        private void FireContactGestureRecognized(PointsCapturedEventArgs pointsInformation, string gestureId, string gestureName)
        {
            List<Point> capturedPoints = SourceDevice == Devices.TouchPad
                ? new List<Point> { _touchPadStartPoint }
                : pointsInformation.FirstCapturedPoints;
            OnGestureRecognized(new RecognitionEventArgs(
                gestureId, gestureName, pointsInformation.Points,
                capturedPoints, _pointsCaptured?.Keys.ToList() ?? new List<int>()));
            OnAfterPointsCaptured(pointsInformation);
        }

        /// <summary>
        /// 尝试锁定用于绘制的特征手指 contactId。
        /// 锁定条件（满足其一）：
        /// 1. 任一手指从起点位移超过 TapDistanceThreshold（50px）
        /// 2. 当前 session 首手指 down 超过 MultiFingerDelay（手指数量已稳定）
        /// 锁定后按 _pointsCaptured 首点 X 坐标排序，用 GetFeatureFingerTrajectoryIndex 选取特征手指。
        /// </summary>
        private void TryLockFeatureFingerForDrawing()
        {
            if (_featureFingerDrawContactId.HasValue || _pointsCaptured == null || _pointsCaptured.Count == 0)
                return;

            // 条件 1：任一手指从起点位移超过 TapDistanceThreshold
            int tapThreshold = AppConfig.TapDistanceThreshold;
            bool hasSignificantMovement = false;
            foreach (var stroke in _pointsCaptured.Values)
            {
                if (stroke.Count >= 2 && PointPatternMath.GetDistance(stroke[0], stroke[^1]) >= tapThreshold)
                {
                    hasSignificantMovement = true;
                    break;
                }
            }

            // 条件 2：首手指 down 超过 MultiFingerDelay
            bool fingerCountSettled = false;
            if (_contactDownTimesMs.Count > 0)
            {
                double firstDownMs = double.MaxValue;
                foreach (var downMs in _contactDownTimesMs.Values)
                {
                    if (downMs < firstDownMs)
                        firstDownMs = downMs;
                }
                fingerCountSettled = GetContactSessionElapsedMs() - firstDownMs >= AppConfig.MultiFingerDelay;
            }

            if (!hasSignificantMovement && !fingerCountSettled)
                return;

            // 按首点 X 坐标排序，选取特征手指
            var sortedEntries = _pointsCaptured
                .OrderBy(kv => kv.Value.Count > 0 ? kv.Value[0].X : int.MaxValue)
                .ToList();
            int trajectoryCount = sortedEntries.Count;
            int featureIndex = GestureManager.GetFeatureFingerTrajectoryIndex(trajectoryCount);
            _featureFingerDrawContactId = sortedEntries[featureIndex].Key;

            // 触摸板：更新偏移参考点为特征手指的起始点，
            // 确保绘制时特征手指轨迹从鼠标位置开始
            if (SourceDevice == Devices.TouchPad && _touchPadReferenceInitialized)
            {
                var featureStroke = sortedEntries[featureIndex].Value;
                if (featureStroke.Count > 0)
                {
                    _touchPadOriginPoint = featureStroke[0];
                }
            }
        }

        private void AddPoint(List<InputPoint> point)
        {
            if (_pointsCaptured == null) return;

            bool getNewPoint = false;
            int threshold = AppConfig.MinimumPointDistance;

            foreach (var p in point)
            {

                // Don't accept point if it's within specified distance of last point unless it's the first point
                if (_pointsCaptured.TryGetValue(p.ContactIdentifier, out List<Point> stroke))
                {
                    // _pointsCaptured 存原始坐标（已是屏幕像素），不做翻译偏移
                    Point actualPoint = p.Point;

                    if (stroke.Count != 0)
                    {
                        double distance = PointPatternMath.GetDistance(stroke.Last(), actualPoint);

                        if (distance < threshold)
                        {
                            continue;
                        }

                        if (State == CaptureState.CapturingInvalid)
                        {
                            State = CaptureState.Capturing;
                        }
                    }

                    getNewPoint = true;
                    // Add point to captured points list
                    stroke.Add(actualPoint);
                }
                else if (_activeContactIds != null
                         && _activeContactIds.Contains(p.ContactIdentifier))
                {
                    // 新手指后到：动态加入 _pointsCaptured
                    var newStroke = new List<Point>(30);
                    newStroke.Add(p.Point);
                    _pointsCaptured[p.ContactIdentifier] = newStroke;
                    _featureFingerIds?.Add(p.ContactIdentifier);
                    getNewPoint = true;
                }
            }
            if (getNewPoint)
            {
                // 尝试锁定特征手指（仅在 DrawFeatureFingerOnly 开启时有意义，但每帧开销极小）
                if (AppConfig.DrawFeatureFingerOnly && !_featureFingerDrawContactId.HasValue)
                {
                    TryLockFeatureFingerForDrawing();
                }

                // Notify subscribers that point has been captured
                var args = new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values), point.Select(p => p.Point).ToList())
                {
                    FingerCount = _peakFingerCount,
                    ActiveFingerCount = _activeContactIds?.Count ?? _peakFingerCount
                };
                args.AllPoints = _allPointsCaptured?.Values.ToList() ?? new List<List<Point>>();
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





