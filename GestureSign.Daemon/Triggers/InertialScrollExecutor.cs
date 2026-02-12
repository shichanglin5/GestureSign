using GestureSign.Common.Applications;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;
using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using WindowsInput;

namespace GestureSign.Daemon.Triggers
{
    /// <summary>
    /// 内置惯性滚动执行器 - 直接在触发器内执行滚动
    /// 从 InertialScrollPlugin 提取核心逻辑，不再经过 Action/Plugin 链路
    /// </summary>
    class InertialScrollExecutor
    {
        #region Native Methods

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

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

        /// <summary>
        /// 处理一帧滚动
        /// </summary>
        public void ProcessFrame(VelocityVector velocity, SystemWindow window, InertialScrollSettings settings)
        {
            if (settings == null) return;

            // 检测新手势会话
            var timeSinceLastGesture = (velocity.Timestamp - _lastGestureTime).TotalMilliseconds;
            if (timeSinceLastGesture > NewGestureThresholdMs)
            {
                _accumulatedX = 0;
                _accumulatedY = 0;
            }
            _lastGestureTime = velocity.Timestamp;

            // WinUI/UWP 应用检测
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

            if (settings.Direction == ScrollDirection.Both && settings.MinorAxisThreshold > 0)
                ApplyJitterFilter(ref deltaX, ref deltaY, settings.MinorAxisThreshold);

            ExecuteScroll(deltaX, deltaY, settings);
        }

        public void Reset()
        {
            _accumulatedX = 0;
            _accumulatedY = 0;
            _lastGestureTime = DateTime.MinValue;
            _cachedWindowHandle = IntPtr.Zero;
            _isWinUIApp = false;
        }

        private void ExecuteScroll(double deltaX, double deltaY, InertialScrollSettings settings)
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

                int scrollDeltaX = (int)(_accumulatedX / settings.PixelsPerScrollUnit * WHEEL_DELTA);
                int scrollDeltaY = (int)(_accumulatedY / settings.PixelsPerScrollUnit * WHEEL_DELTA);

                if (scrollDeltaX != 0)
                    _accumulatedX -= scrollDeltaX * settings.PixelsPerScrollUnit / WHEEL_DELTA;
                if (scrollDeltaY != 0)
                    _accumulatedY -= scrollDeltaY * settings.PixelsPerScrollUnit / WHEEL_DELTA;

                if (_isWinUIApp && settings.EnableWinUIDetection)
                {
                    if (scrollDeltaY != 0)
                        SendWheelMessageToWindow(_cachedWindowHandle, scrollDeltaY, isHorizontal: false);
                    if (scrollDeltaX != 0)
                        SendWheelMessageToWindow(_cachedWindowHandle, scrollDeltaX, isHorizontal: true);
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

        private static void ApplyJitterFilter(ref double deltaX, ref double deltaY, double threshold)
        {
            double absDeltaX = Math.Abs(deltaX);
            double absDeltaY = Math.Abs(deltaY);

            if (absDeltaX == 0 || absDeltaY == 0)
                return;

            bool isMainlyVertical = absDeltaY >= absDeltaX;
            double mainDelta = isMainlyVertical ? absDeltaY : absDeltaX;
            double minorDelta = isMainlyVertical ? absDeltaX : absDeltaY;

            if (minorDelta / mainDelta < threshold)
            {
                if (isMainlyVertical)
                    deltaX = 0;
                else
                    deltaY = 0;
            }
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
