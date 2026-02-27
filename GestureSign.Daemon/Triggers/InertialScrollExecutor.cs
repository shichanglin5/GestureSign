using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Timers;
using System.Windows.Automation;
using WindowsInput;

namespace GestureSign.Daemon.Triggers
{
    /// <summary>
/// Inertial scroll executor running inside trigger pipeline.
/// Core logic extracted from InertialScrollPlugin.
/// </summary>
    class InertialScrollExecutor
    {
        #region Native Methods

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPointEx(IntPtr hwndParent, POINT pt, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint GA_ROOT = 2;
        private const uint WM_MOUSEWHEEL = 0x020A;
        private const uint WM_MOUSEHWHEEL = 0x020E;
        private const uint CWP_SKIPINVISIBLE = 0x0001;
        private const uint CWP_SKIPDISABLED = 0x0004;
        private const uint CWP_SKIPTRANSPARENT = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        #endregion

        private static readonly InputSimulator _inputSimulator = new InputSimulator();

        private double _accumulatedX;
        private double _accumulatedY;
        private DateTime _lastGestureTime = DateTime.MinValue;
        private const int NewGestureThresholdMs = 200;

        private bool _isWinUIApp;
        private IntPtr _cachedWindowHandle;

        private readonly object _lock = new object();
        private Timer _inertiaTimer;
        private volatile bool _isInertiaActive;
        private double _inertiaStartVelocityX;
        private double _inertiaStartVelocityY;
        private readonly Stopwatch _inertiaStopwatch = new Stopwatch();
        private long _lastInertiaTickMs;
        private POINT _inertiaStartCursorPos;
        private IntPtr _inertiaTargetWindow;
        private InertialScrollSettings _lastSettings;

        // 触控屏滚动目标：触控屏以手指触摸点为滚动目标，触控板以鼠标位置为目标
        private bool _isTouchScreen;
        private POINT _touchScreenPoint;

        // V2 方向状态机：窗口比例判定 + 滞回
        private enum DirectionState { Undecided, LockX, LockY, Free2D }
        private DirectionState _directionState = DirectionState.Undecided;

        // 窗口缓冲：存储最近 windowMs 内的原始位移绝对值
        private const int WindowMs = 60;
        private const int MaxWindowSamples = 16;
        private const double StartDistancePx = 8.0;
        private readonly Queue<(DateTime timestamp, double absRawDeltaX, double absRawDeltaY)> _windowBuffer = new();

        // Undecided 阶段缓冲：存储已变换 delta，状态确定后回补
        private readonly Queue<(double deltaX, double deltaY)> _undecidedBuffer = new();

        /// <summary>
/// Inertial scroll executor running inside trigger pipeline.
/// Core logic extracted from InertialScrollPlugin.
/// </summary>
        public void ProcessFrame(VelocityVector velocity, SystemWindow window, InertialScrollSettings settings,
            Devices sourceDevice = Devices.None, Point touchPoint = default)
        {
            if (settings == null) return;
            // 在获取 lock 之前先标记取消惯性，让 OnInertiaTick 尽快退出释放 lock
            _isInertiaActive = false;
            _inertiaTimer?.Stop();
            lock (_lock)
            {
                StopInertiaInternal();
                _lastSettings = settings;
                _inertiaTargetWindow = window?.HWnd ?? IntPtr.Zero;
                _isTouchScreen = sourceDevice.HasFlag(Devices.TouchScreen);
                if (_isTouchScreen)
                    _touchScreenPoint = new POINT { X = touchPoint.X, Y = touchPoint.Y };

                var timeSinceLastGesture = (velocity.Timestamp - _lastGestureTime).TotalMilliseconds;
                if (timeSinceLastGesture > NewGestureThresholdMs)
                {
                    _accumulatedX = 0;
                    _accumulatedY = 0;
                    _directionState = DirectionState.Undecided;
                    _windowBuffer.Clear();
                    _undecidedBuffer.Clear();
                }
                _lastGestureTime = velocity.Timestamp;

                if (settings.EnableWinUIDetection)
                {
                    var windowHandle = window?.HWnd ?? IntPtr.Zero;
                    if (windowHandle != _cachedWindowHandle)
                    {
                        _cachedWindowHandle = windowHandle;
                        _isWinUIApp = IsWinUIOrUWPApp(window);
                    }
                }

                if (velocity.DeltaX == 0 && velocity.DeltaY == 0)
                    return;

                double speedMultiplier = CalculateSpeedMultiplier(velocity.Magnitude, settings.AccelerationFactor);

                double verticalMultiplier = speedMultiplier * (settings.ReverseDirection ? -1 : 1);
                double horizontalMultiplier = speedMultiplier * (settings.ReverseHorizontalDirection ? 1 : -1);

                double deltaX = velocity.DeltaX * horizontalMultiplier;
                double deltaY = velocity.DeltaY * verticalMultiplier;

                if (settings.Direction == ScrollDirection.Vertical)
                    deltaX = 0;
                else if (settings.Direction == ScrollDirection.Horizontal)
                    deltaY = 0;

                if (settings.Direction == ScrollDirection.Both && settings.NoiseRatio > 0)
                {
                    ApplyDirectionStateMachine(ref deltaX, ref deltaY,
                        velocity.DeltaX, velocity.DeltaY, velocity.Timestamp, settings);
                    if (_directionState == DirectionState.Undecided)
                        return; // delta 已缓冲，不输出滚动
                }

                ExecuteScroll(deltaX, deltaY, settings);
            }
        }

        public void Reset()
        {
            StopInertia();
            ResetGestureState();
            _directionState = DirectionState.Undecided;
            _windowBuffer.Clear();
            _undecidedBuffer.Clear();
            _cachedWindowHandle = IntPtr.Zero;
            _isWinUIApp = false;
            _isTouchScreen = false;
            _lastSettings = null;
            if (_inertiaTimer != null)
            {
                _inertiaTimer.Dispose();
                _inertiaTimer = null;
            }
        }

        public void ResetGestureState()
        {
            _accumulatedX = 0;
            _accumulatedY = 0;
            _lastGestureTime = DateTime.MinValue;
            // 注意：不重置 _directionState，需要保留到惯性阶段结束。
            // 在 ProcessFrame 的新手势检测（timeSinceLastGesture > NewGestureThresholdMs）中重置。
        }

        public void StopInertia()
        {
            lock (_lock)
            {
                StopInertiaInternal();
            }
        }

        public void StartInertiaIfNeeded(VelocityVector lastVelocity, SystemWindow window, InertialScrollSettings settings,
            Devices sourceDevice = Devices.None, Point touchPoint = default)
        {
            lock (_lock)
            {
                if (settings == null || !settings.EnableMomentum)
                    return;

                // Undecided 状态：手势未产生有效滚动方向，不启动惯性
                if (_directionState == DirectionState.Undecided &&
                    settings.Direction == ScrollDirection.Both && settings.NoiseRatio > 0)
                {
                    _undecidedBuffer.Clear();
                    return;
                }

                double magnitude = lastVelocity.Magnitude;
                if (magnitude < settings.MomentumMinVelocity)
                    return;

                _lastSettings = settings;
                _inertiaTargetWindow = window?.HWnd ?? IntPtr.Zero;
                _isTouchScreen = sourceDevice.HasFlag(Devices.TouchScreen);
                if (_isTouchScreen)
                    _touchScreenPoint = new POINT { X = touchPoint.X, Y = touchPoint.Y };
                _inertiaStartVelocityX = lastVelocity.VelocityX;
                _inertiaStartVelocityY = lastVelocity.VelocityY;
                _inertiaStopwatch.Restart();
                _lastInertiaTickMs = 0;
                GetCursorPos(out _inertiaStartCursorPos);
                _isInertiaActive = true;

                EnsureInertiaTimer();
                double interval = Math.Max(8.0, _lastSettings.MomentumTickMs);
                if (interval > 33.0) interval = 33.0;
                _inertiaTimer.Interval = interval;
                _inertiaTimer.Start();
            }
        }
        private void ExecuteScroll(double deltaX, double deltaY, InertialScrollSettings settings, double pixelsPerScrollUnitOverride = 0)
        {
            try
            {
                if (_isWinUIApp && settings.EnableWinUIDetection)
                {
                    deltaX *= settings.WinUIScrollMultiplier;
                    deltaY *= settings.WinUIScrollMultiplier;
                }

                _accumulatedX += deltaX;
                _accumulatedY += deltaY;

                const int WHEEL_DELTA = 120;
                double pixelsPerUnit = pixelsPerScrollUnitOverride > 0 ? pixelsPerScrollUnitOverride : settings.PixelsPerScrollUnit;

                int scrollDeltaX = (int)(_accumulatedX / pixelsPerUnit * WHEEL_DELTA);
                int scrollDeltaY = (int)(_accumulatedY / pixelsPerUnit * WHEEL_DELTA);

                if (scrollDeltaX != 0)
                    _accumulatedX -= scrollDeltaX * pixelsPerUnit / WHEEL_DELTA;
                if (scrollDeltaY != 0)
                    _accumulatedY -= scrollDeltaY * pixelsPerUnit / WHEEL_DELTA;

                if (_isWinUIApp && settings.EnableWinUIDetection)
                {
                    if (scrollDeltaY != 0)
                        SendWheelMessageToWindow(_cachedWindowHandle, scrollDeltaY, isHorizontal: false);
                    if (scrollDeltaX != 0)
                        SendWheelMessageToWindow(_cachedWindowHandle, scrollDeltaX, isHorizontal: true);
                }
                else if (_isTouchScreen)
                {
                    // 触控屏：发送滚轮消息到触摸点位置的窗口
                    if (scrollDeltaY != 0)
                        PostWheelMessageToPoint(_touchScreenPoint, scrollDeltaY, isHorizontal: false);
                    if (scrollDeltaX != 0)
                        PostWheelMessageToPoint(_touchScreenPoint, scrollDeltaX, isHorizontal: true);
                }
                else
                {
                    // 触控板：SendInput 发送滚轮事件，由系统路由到鼠标光标位置的窗口
                    if (scrollDeltaY != 0)
                        SendScrollDelta(scrollDeltaY, isHorizontal: false);
                    if (scrollDeltaX != 0)
                        SendScrollDelta(scrollDeltaX, isHorizontal: true);
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        private static void SendScrollDelta(int delta, bool isHorizontal)
        {
            try
            {
                if (isHorizontal)
                    _inputSimulator.Mouse.HorizontalScrollDelta(delta);
                else
                    _inputSimulator.Mouse.VerticalScrollDelta(delta);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        /// <summary>
        /// 触控屏专用：向指定屏幕坐标处的窗口发送滚轮消息。
        /// 先用 WindowFromPoint 找到顶层窗口，再用 ChildWindowFromPointEx 找到实际子窗口。
        /// </summary>
        private static void PostWheelMessageToPoint(POINT screenPoint, int delta, bool isHorizontal)
        {
            try
            {
                IntPtr hwnd = WindowFromPoint(screenPoint);
                if (hwnd == IntPtr.Zero) return;

                // 尝试找到更精确的子窗口
                POINT clientPoint = screenPoint;
                if (ScreenToClient(hwnd, ref clientPoint))
                {
                    IntPtr childHwnd = ChildWindowFromPointEx(hwnd, clientPoint,
                        CWP_SKIPINVISIBLE | CWP_SKIPDISABLED | CWP_SKIPTRANSPARENT);
                    if (childHwnd != IntPtr.Zero && childHwnd != hwnd)
                        hwnd = childHwnd;
                }

                uint msg = isHorizontal ? WM_MOUSEHWHEEL : WM_MOUSEWHEEL;
                // wParam: HIWORD = delta, LOWORD = key state (0)
                IntPtr wParam = (IntPtr)(delta << 16);
                // lParam: LOWORD = x, HIWORD = y (screen coordinates)
                IntPtr lParam = (IntPtr)((screenPoint.Y << 16) | (screenPoint.X & 0xFFFF));

                PostMessage(hwnd, msg, wParam, lParam);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        private void SendWheelMessageToWindow(IntPtr hwnd, int delta, bool isHorizontal)
        {
            try
            {
                GetCursorPos(out POINT pt);
                var element = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
                if (element != null)
                {
                    var scrollElement = FindScrollableElement(element);
                    if (scrollElement != null)
                    {
                        var scrollPattern = scrollElement.GetCurrentPattern(ScrollPattern.Pattern) as ScrollPattern;
                        if (scrollPattern != null)
                        {
                            int scrollUnits = Math.Abs(delta) / 120;
                            if (scrollUnits == 0) scrollUnits = 1;

                            ScrollAmount amount = delta > 0 ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement;

                            if (isHorizontal)
                            {
                                if (scrollPattern.Current.HorizontallyScrollable)
                                    for (int i = 0; i < scrollUnits; i++)
                                        scrollPattern.Scroll(amount, ScrollAmount.NoAmount);
                            }
                            else
                            {
                                if (scrollPattern.Current.VerticallyScrollable)
                                    for (int i = 0; i < scrollUnits; i++)
                                        scrollPattern.Scroll(ScrollAmount.NoAmount, amount);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        private static AutomationElement FindScrollableElement(AutomationElement element)
        {
            var current = element;
            int maxDepth = 10;

            while (current != null && maxDepth-- > 0)
            {
                try
                {
                    var scrollPattern = current.GetCurrentPattern(ScrollPattern.Pattern) as ScrollPattern;
                    if (scrollPattern != null &&
                        (scrollPattern.Current.VerticallyScrollable || scrollPattern.Current.HorizontallyScrollable))
                        return current;
                }
                catch (InvalidOperationException) { }

                var walker = TreeWalker.ControlViewWalker;
                current = walker.GetParent(current);
            }
            return null;
        }

        private static double CalculateSpeedMultiplier(double velocity, double accelerationFactor)
        {
            double absVelocity = Math.Abs(velocity);

            if (absVelocity < 100)
                return 5.0;
            else if (absVelocity < 500)
            {
                double t = (absVelocity - 100) / 400.0;
                return 5.0 - 3.0 * t;
            }
            else if (absVelocity < 2000)
            {
                double t = (absVelocity - 500) / 1500.0;
                return 2.0 - 1.0 * t;
            }
            else
            {
                double velocityRatio = absVelocity / 2000.0;
                double exponent = Math.Log(velocityRatio, 2.0) * 0.3;
                return Math.Pow(accelerationFactor, exponent);
            }
        }

        /// <summary>
        /// V2 方向状态机：基于窗口内原始位移比例 + 滞回判定方向锁定。
        /// Undecided 阶段缓冲 delta，状态确定后回补。
        /// </summary>
        private void ApplyDirectionStateMachine(ref double deltaX, ref double deltaY,
            double rawDeltaX, double rawDeltaY, DateTime timestamp, InertialScrollSettings settings)
        {
            // 1. 更新窗口缓冲
            _windowBuffer.Enqueue((timestamp, Math.Abs(rawDeltaX), Math.Abs(rawDeltaY)));
            while (_windowBuffer.Count > MaxWindowSamples)
                _windowBuffer.Dequeue();
            var cutoff = timestamp.AddMilliseconds(-WindowMs);
            while (_windowBuffer.Count > 0 && _windowBuffer.Peek().timestamp < cutoff)
                _windowBuffer.Dequeue();

            // 2. 计算窗口内累积
            double sumX = 0, sumY = 0;
            foreach (var sample in _windowBuffer)
            {
                sumX += sample.absRawDeltaX;
                sumY += sample.absRawDeltaY;
            }
            double major = Math.Max(sumX, sumY);
            double minor = Math.Min(sumX, sumY);
            double ratio = major > 0 ? minor / major : 0;

            double noiseRatio = Math.Max(0.01, Math.Min(0.99, settings.NoiseRatio));
            double ratEnter = noiseRatio;
            double ratExit = Math.Min(0.6, noiseRatio + 0.10);

            // 3. 状态机切换
            var prevState = _directionState;
            switch (_directionState)
            {
                case DirectionState.Undecided:
                    if (major >= StartDistancePx)
                    {
                        if (ratio < ratEnter)
                            _directionState = sumX > sumY ? DirectionState.LockX : DirectionState.LockY;
                        else
                            _directionState = DirectionState.Free2D;
                    }
                    break;

                case DirectionState.LockX:
                case DirectionState.LockY:
                    if (ratio > ratExit)
                        _directionState = DirectionState.Free2D;
                    break;

                case DirectionState.Free2D:
                    // 第一版不回切，保持 Free2D 直到手势结束
                    break;
            }

            // 4. 如果刚从 Undecided 转出，回补缓冲的 delta
            if (prevState == DirectionState.Undecided && _directionState != DirectionState.Undecided)
            {
                // 先缓冲当帧（因为当帧 delta 也尚未输出）
                _undecidedBuffer.Enqueue((deltaX, deltaY));

                // 回放所有缓冲帧，按目标状态过滤后逐帧 ExecuteScroll
                while (_undecidedBuffer.Count > 0)
                {
                    var (bufDeltaX, bufDeltaY) = _undecidedBuffer.Dequeue();
                    ApplyDirectionSuppression(ref bufDeltaX, ref bufDeltaY);
                    ExecuteScroll(bufDeltaX, bufDeltaY, settings);
                }

                // 当帧已在回补中处理，通知调用者跳过本帧的 ExecuteScroll
                deltaX = 0;
                deltaY = 0;
                return;
            }

            // 5. 仍在 Undecided：缓冲当帧，不输出
            if (_directionState == DirectionState.Undecided)
            {
                _undecidedBuffer.Enqueue((deltaX, deltaY));
                // 防止异常堆积
                while (_undecidedBuffer.Count > MaxWindowSamples)
                    _undecidedBuffer.Dequeue();
                deltaX = 0;
                deltaY = 0;
                return;
            }

            // 6. 已确定方向：应用抑制
            ApplyDirectionSuppression(ref deltaX, ref deltaY);
        }

        /// <summary>
        /// 按当前方向状态抑制次轴 delta。
        /// </summary>
        private void ApplyDirectionSuppression(ref double deltaX, ref double deltaY)
        {
            switch (_directionState)
            {
                case DirectionState.LockX:
                    deltaY = 0;
                    break;
                case DirectionState.LockY:
                    deltaX = 0;
                    break;
                case DirectionState.Undecided:
                    // Undecided 状态在惯性阶段不应出现（不启动惯性），但保险起见不过滤
                    break;
                case DirectionState.Free2D:
                    // 不处理
                    break;
            }
        }

        private void EnsureInertiaTimer()
        {
            if (_inertiaTimer != null)
                return;

            _inertiaTimer = new Timer
            {
                AutoReset = true,
                Enabled = false
            };
            _inertiaTimer.Elapsed += OnInertiaTick;
        }

        private void OnInertiaTick(object sender, ElapsedEventArgs e)
        {
            // 在 lock 内仅做计算和累积器更新，出 lock 后执行实际滚动发送
            // 避免 UIA/PostMessage 等耗时操作长时间持有 lock
            int scrollDeltaX = 0, scrollDeltaY = 0;
            bool isWinUI = false;
            bool isTouchScreen = false;
            IntPtr cachedHwnd = IntPtr.Zero;
            POINT touchPoint = default;
            InertialScrollSettings settings = null;

            lock (_lock)
            {
                if (!_isInertiaActive || _lastSettings == null)
                    return;

                if (_inertiaTargetWindow != IntPtr.Zero &&
                    GetAncestor(GetForegroundWindow(), GA_ROOT) != GetAncestor(_inertiaTargetWindow, GA_ROOT))
                {
                    StopInertiaInternal();
                    return;
                }

                if (GetCursorPos(out POINT currentPos))
                {
                    int dx = currentPos.X - _inertiaStartCursorPos.X;
                    int dy = currentPos.Y - _inertiaStartCursorPos.Y;
                    if ((dx * dx + dy * dy) > 36)
                    {
                        StopInertiaInternal();
                        return;
                    }
                }

                long elapsedMs = _inertiaStopwatch.ElapsedMilliseconds;
                if (_lastSettings.MomentumMaxDurationMs > 0 &&
                    elapsedMs > _lastSettings.MomentumMaxDurationMs)
                {
                    StopInertiaInternal();
                    return;
                }

                double tau = _lastSettings.MomentumTimeConstantMs;
                if (tau <= 0)
                {
                    StopInertiaInternal();
                    return;
                }

                double decay = Math.Exp(-elapsedMs / tau);
                double vx = _inertiaStartVelocityX * decay;
                double vy = _inertiaStartVelocityY * decay;
                double magnitude = Math.Sqrt(vx * vx + vy * vy);

                if (magnitude < _lastSettings.MomentumMinVelocity)
                {
                    StopInertiaInternal();
                    return;
                }

                long deltaMs = elapsedMs - _lastInertiaTickMs;
                if (deltaMs <= 0)
                    return;

                double maxStepMs = Math.Max(8.0, _lastSettings.MomentumTickMs) * 3.0;
                if (deltaMs > maxStepMs)
                    deltaMs = (long)maxStepMs;

                _lastInertiaTickMs = elapsedMs;
                double dtSeconds = deltaMs / 1000.0;

                double frameDeltaX = vx * dtSeconds;
                double frameDeltaY = vy * dtSeconds;

                // 惯性阶段 speedMultiplier 限制为 <=1.0，避免低速时的放大导致尾部衰减不下去
                double speedMultiplier = Math.Min(1.0, CalculateSpeedMultiplier(magnitude, _lastSettings.AccelerationFactor));
                double verticalMultiplier = speedMultiplier * (_lastSettings.ReverseDirection ? -1 : 1);
                double horizontalMultiplier = speedMultiplier * (_lastSettings.ReverseHorizontalDirection ? 1 : -1);

                double deltaX = frameDeltaX * horizontalMultiplier;
                double deltaY = frameDeltaY * verticalMultiplier;

                if (_lastSettings.Direction == ScrollDirection.Vertical)
                    deltaX = 0;
                else if (_lastSettings.Direction == ScrollDirection.Horizontal)
                    deltaY = 0;

                // 惯性阶段复用手势末态的方向状态
                if (_lastSettings.Direction == ScrollDirection.Both && _lastSettings.NoiseRatio > 0)
                {
                    ApplyDirectionSuppression(ref deltaX, ref deltaY);
                }

                // 在 lock 内完成累积器计算，得出实际滚轮 delta
                settings = _lastSettings;

                if (_isWinUIApp && settings.EnableWinUIDetection)
                {
                    deltaX *= settings.WinUIScrollMultiplier;
                    deltaY *= settings.WinUIScrollMultiplier;
                }

                _accumulatedX += deltaX;
                _accumulatedY += deltaY;

                const int WHEEL_DELTA = 120;
                double pixelsPerUnit = settings.PixelsPerScrollUnit;

                scrollDeltaX = (int)(_accumulatedX / pixelsPerUnit * WHEEL_DELTA);
                scrollDeltaY = (int)(_accumulatedY / pixelsPerUnit * WHEEL_DELTA);

                if (scrollDeltaX != 0)
                    _accumulatedX -= scrollDeltaX * pixelsPerUnit / WHEEL_DELTA;
                if (scrollDeltaY != 0)
                    _accumulatedY -= scrollDeltaY * pixelsPerUnit / WHEEL_DELTA;

                if (scrollDeltaX == 0 && scrollDeltaY == 0)
                    return;

                // 快照发送路由所需的状态
                isWinUI = _isWinUIApp && settings.EnableWinUIDetection;
                isTouchScreen = _isTouchScreen;
                cachedHwnd = _cachedWindowHandle;
                touchPoint = _touchScreenPoint;
            }

            // lock 外执行实际的滚动发送（可能涉及 UIA、PostMessage 等耗时操作）
            try
            {
                if (isWinUI)
                {
                    if (scrollDeltaY != 0)
                        SendWheelMessageToWindow(cachedHwnd, scrollDeltaY, isHorizontal: false);
                    if (scrollDeltaX != 0)
                        SendWheelMessageToWindow(cachedHwnd, scrollDeltaX, isHorizontal: true);
                }
                else if (isTouchScreen)
                {
                    if (scrollDeltaY != 0)
                        PostWheelMessageToPoint(touchPoint, scrollDeltaY, isHorizontal: false);
                    if (scrollDeltaX != 0)
                        PostWheelMessageToPoint(touchPoint, scrollDeltaX, isHorizontal: true);
                }
                else
                {
                    if (scrollDeltaY != 0)
                        SendScrollDelta(scrollDeltaY, isHorizontal: false);
                    if (scrollDeltaX != 0)
                        SendScrollDelta(scrollDeltaX, isHorizontal: true);
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }
        private void StopInertiaInternal()
        {
            if (!_isInertiaActive)
                return;

            _isInertiaActive = false;
            if (_inertiaTimer != null)
                _inertiaTimer.Stop();
        }

        private bool IsWinUIOrUWPApp(SystemWindow window)
        {
            if (window == null) return false;

            try
            {
                var topWindow = window;
                while (topWindow.Parent != null && topWindow.Parent.HWnd != IntPtr.Zero)
                    topWindow = topWindow.Parent;

                string className = topWindow.ClassName;

                if ("ApplicationFrameWindow".Equals(className, StringComparison.Ordinal) ||
                    "Windows.UI.Core.CoreWindow".Equals(className, StringComparison.Ordinal) ||
                    "WinUIDesktopWin32WindowClass".Equals(className, StringComparison.Ordinal))
                    return true;

                try
                {
                    string processName = topWindow.Process?.ProcessName;
                    if (string.IsNullOrEmpty(processName)) return false;

                    string[] winUIProcesses = {
                        "SystemSettings", "PowerToys.Settings", "WinStore.App",
                        "PhoneExperienceHost", "WindowsTerminal", "DevHome", "ms-teams"
                    };

                    foreach (var name in winUIProcesses)
                        if (processName.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return true;
                }
                catch { }

                return false;
            }
            catch { return false; }
        }
    }
}












