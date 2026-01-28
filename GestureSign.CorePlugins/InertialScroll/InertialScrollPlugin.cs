using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using WindowsInput;
using GestureSign.Common.Localization;
using GestureSign.Common.Plugins;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;

namespace GestureSign.CorePlugins.InertialScroll
{
    /// <summary>
    /// 惯性滚动插件 - 基于双指滑动速度实现跟手的滚动效果
    /// </summary>
    public class InertialScrollPlugin : IPlugin
    {
        #region Native Methods

        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;

        // mouse_event flags
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x1000;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        #endregion

        #region Private Variables

        private InertialScrollSettings _settings = null;
        private InertialScrollUI _gui = null;
        private static readonly InputSimulator _inputSimulator = new InputSimulator();

        // 位移累积器 - 用于累积小的位移直到达到滚动阈值
        private double _accumulatedX = 0;
        private double _accumulatedY = 0;

        // 上次手势时间戳 - 用于检测新手势会话
        private DateTime _lastGestureTime = DateTime.MinValue;
        private const int NewGestureThresholdMs = 200;

        // WinUI/UWP 应用检测缓存
        private bool _isWinUIApp;
        private IntPtr _cachedWindowHandle;

        #endregion

        #region IPlugin Properties

        public string Name =>
            LocalizationProvider.Instance.GetTextValue("CorePlugins.InertialScroll.Name");

        public string Category =>
            LocalizationProvider.Instance.GetTextValue("CorePlugins.MouseActions.Category");

        public string Description => GetDescription();

        public bool IsAction => true;

        public object GUI => _gui ?? (_gui = CreateGUI());

        public bool ActivateWindowDefault => false;

        public object Icon => IconSource.Mouse;  // 使用鼠标图标表示滚动

        public IHostControl HostControl { get; set; }

        #endregion

        #region IPlugin Methods

        public void Initialize() { }

        public bool Gestured(PointInfo actionPoint)
        {
            // 实时从 GUI 获取最新设置（如果 GUI 存在）
            if (_gui != null)
                _settings = _gui.Settings;

            if (_settings == null)
            {
                Logging.LogWarning("[InertialScrollPlugin] Settings is null");
                return false;
            }

            // 检查是否有速度信息
            if (actionPoint.Velocity == null)
            {
                Logging.LogWarning("[InertialScrollPlugin] No velocity information available");
                return false;
            }

            var velocity = actionPoint.Velocity.Value;

            // 检测新手势会话 - 如果时间间隔超过阈值，重置累积器
            var timeSinceLastGesture = (velocity.Timestamp - _lastGestureTime).TotalMilliseconds;
            if (timeSinceLastGesture > NewGestureThresholdMs)
            {
                _accumulatedX = 0;
                _accumulatedY = 0;
            }
            _lastGestureTime = velocity.Timestamp;

            // 检测目标窗口是否为 WinUI/UWP 应用（新手势会话或窗口变化时重新检测）
            if (_settings.EnableWinUIDetection)
            {
                var window = actionPoint.Window;
                var windowHandle = window?.HWnd ?? IntPtr.Zero;
                if (windowHandle != _cachedWindowHandle)
                {
                    _cachedWindowHandle = windowHandle;
                    _isWinUIApp = IsWinUIOrUWPApp(window);
                    if (_isWinUIApp)
                    {
                        Logging.LogDebug($"[InertialScroll] WinUI multiplier active: {_settings.WinUIScrollMultiplier}x");
                    }
                }
            }

            // 基于位移判断 - 只要有位移就滚动，不再检查速度阈值
            // 这样即使慢速滑动也能响应，实现真正的跟手滚动
            if (velocity.DeltaX == 0 && velocity.DeltaY == 0)
            {
                return false;
            }

            // 计算非线性速度倍数 - 慢速放大，快速根据加速因子调整
            double speedMultiplier = CalculateSpeedMultiplier(velocity.Magnitude);

            // 应用方向过滤和速度曲线
            // 垂直：默认反向（向上滑动页面向下滚，符合触控板习惯）
            // 水平：默认同向（向右滑动页面向右滚，符合触屏习惯）
            double verticalMultiplier = speedMultiplier * (_settings.ReverseDirection ? -1 : 1);
            double horizontalMultiplier = speedMultiplier * (_settings.ReverseHorizontalDirection ? 1 : -1);

            double deltaX = velocity.DeltaX * horizontalMultiplier;
            double deltaY = velocity.DeltaY * verticalMultiplier;

            if (_settings.Direction == ScrollDirection.Vertical)
                deltaX = 0;
            else if (_settings.Direction == ScrollDirection.Horizontal)
                deltaY = 0;

            // 应用抖动过滤（仅在双向模式下）
            if (_settings.Direction == ScrollDirection.Both && _settings.MinorAxisThreshold > 0)
            {
                ApplyJitterFilter(ref deltaX, ref deltaY);
            }

            // 连续手势会频繁触发（每帧一次），所以直接执行即时滚动
            // 使用实际位移实现跟手效果
            ExecuteSimpleScroll(deltaX, deltaY, velocity.Magnitude, speedMultiplier);

            return true;
        }

        public bool Deserialize(string serializedData)
        {
            return PluginHelper.DeserializeSettings(serializedData, out _settings);
        }

        public string Serialize()
        {
            if (_gui != null)
                _settings = _gui.Settings;

            if (_settings == null)
                _settings = new InertialScrollSettings();

            return PluginHelper.SerializeSettings(_settings);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 执行即时滚动 - 使用累积器实现平滑跟手效果（高精度滚动）
        /// </summary>
        /// <param name="deltaX">水平位移（像素）</param>
        /// <param name="deltaY">垂直位移（像素）</param>
        /// <param name="velocityMagnitude">速度大小（用于日志）</param>
        /// <param name="speedMultiplier">速度倍数（用于日志）</param>
        private void ExecuteSimpleScroll(double deltaX, double deltaY, double velocityMagnitude = 0, double speedMultiplier = 1)
        {
            try
            {
                // WinUI/UWP 应用滚动补偿
                if (_isWinUIApp && _settings.EnableWinUIDetection)
                {
                    deltaX *= _settings.WinUIScrollMultiplier;
                    deltaY *= _settings.WinUIScrollMultiplier;
                }

                // 累积位移
                _accumulatedX += deltaX;
                _accumulatedY += deltaY;

                // WHEEL_DELTA = 120 是标准鼠标滚轮一格的值
                const int WHEEL_DELTA = 120;

                // 高精度滚动，将像素转换为原始滚轮delta值
                // PixelsPerScrollUnit 表示多少像素等于一个标准滚轮单位(120)
                int scrollDeltaX = (int)(_accumulatedX / _settings.PixelsPerScrollUnit * WHEEL_DELTA);
                int scrollDeltaY = (int)(_accumulatedY / _settings.PixelsPerScrollUnit * WHEEL_DELTA);

                // 从累积器中扣除已转换的部分（保留小数部分以提高精度）
                if (scrollDeltaX != 0)
                    _accumulatedX -= scrollDeltaX * _settings.PixelsPerScrollUnit / WHEEL_DELTA;
                if (scrollDeltaY != 0)
                    _accumulatedY -= scrollDeltaY * _settings.PixelsPerScrollUnit / WHEEL_DELTA;

                if (_isWinUIApp && _settings.EnableWinUIDetection)
                {
                    // WinUI/UWP 应用：使用专用方法（优先 UI Automation，回退 mouse_event）
                    if (scrollDeltaY != 0)
                        SendWheelMessageToWindow(_cachedWindowHandle, scrollDeltaY, isHorizontal: false);
                    if (scrollDeltaX != 0)
                        SendWheelMessageToWindow(_cachedWindowHandle, scrollDeltaX, isHorizontal: true);
                }
                else
                {
                    // 普通应用：使用 SendInput 高精度滚动
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

        /// <summary>
        /// 发送高精度滚动消息 - 使用原始wheel delta值
        /// </summary>
        private static void SendScrollDelta(int delta, bool isHorizontal)
        {
            try
            {
                if (isHorizontal)
                {
                    _inputSimulator.Mouse.HorizontalScrollDelta(delta);
                }
                else
                {
                    _inputSimulator.Mouse.VerticalScrollDelta(delta);
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        /// <summary>
        /// 使用 UI Automation 滚动 WinUI/UWP 应用
        /// </summary>
        private void SendWheelMessageToWindow(IntPtr hwnd, int delta, bool isHorizontal)
        {
            try
            {
                // 获取当前鼠标位置
                GetCursorPos(out POINT pt);

                // 使用 UI Automation 滚动
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
                                {
                                    for (int i = 0; i < scrollUnits; i++)
                                    {
                                        scrollPattern.Scroll(amount, ScrollAmount.NoAmount);
                                    }
                                }
                            }
                            else
                            {
                                if (scrollPattern.Current.VerticallyScrollable)
                                {
                                    for (int i = 0; i < scrollUnits; i++)
                                    {
                                        scrollPattern.Scroll(ScrollAmount.NoAmount, amount);
                                    }
                                }
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

        /// <summary>
        /// 向上遍历 UI 树，找到支持滚动的元素
        /// </summary>
        private static AutomationElement FindScrollableElement(AutomationElement element)
        {
            var current = element;
            int maxDepth = 10; // 防止无限循环

            while (current != null && maxDepth-- > 0)
            {
                try
                {
                    var scrollPattern = current.GetCurrentPattern(ScrollPattern.Pattern) as ScrollPattern;
                    if (scrollPattern != null &&
                        (scrollPattern.Current.VerticallyScrollable || scrollPattern.Current.HorizontallyScrollable))
                    {
                        return current;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Pattern not supported, continue to parent
                }

                var walker = TreeWalker.ControlViewWalker;
                current = walker.GetParent(current);
            }

            return null;
        }

        private InertialScrollUI CreateGUI()
        {
            var newGUI = new InertialScrollUI();
            newGUI.Loaded += (o, e) =>
            {
                if (_settings != null)
                    newGUI.Settings = _settings;
            };
            return newGUI;
        }

        private string GetDescription()
        {
            if (_settings == null)
                return "Scroll";

            string direction = _settings.Direction switch
            {
                ScrollDirection.Vertical => "Vertical",
                ScrollDirection.Horizontal => "Horizontal",
                _ => "Auto"
            };

            return $"Scroll ({direction}, Accel={_settings.AccelerationFactor:F1}x)";
        }

        /// <summary>
        /// 应用抖动过滤 - 主方向优先算法
        /// 当次方向位移占比小于阈值时，忽略次方向滚动
        /// </summary>
        /// <param name="deltaX">水平位移（会被修改）</param>
        /// <param name="deltaY">垂直位移（会被修改）</param>
        private void ApplyJitterFilter(ref double deltaX, ref double deltaY)
        {
            double absDeltaX = Math.Abs(deltaX);
            double absDeltaY = Math.Abs(deltaY);

            // 如果某个方向为零，无需过滤
            if (absDeltaX == 0 || absDeltaY == 0)
                return;

            // 确定主方向和次方向
            bool isMainlyVertical = absDeltaY >= absDeltaX;
            double mainDelta = isMainlyVertical ? absDeltaY : absDeltaX;
            double minorDelta = isMainlyVertical ? absDeltaX : absDeltaY;

            // 计算次方向占比
            double minorRatio = minorDelta / mainDelta;

            // 如果次方向占比太小，归零次方向
            if (minorRatio < _settings.MinorAxisThreshold)
            {
                if (isMainlyVertical)
                    deltaX = 0;  // 主要是垂直滚动，忽略水平抖动
                else
                    deltaY = 0;  // 主要是水平滚动，忽略垂直抖动
            }
        }

        /// <summary>
        /// 非线性速度映射 - 慢速放大，快速根据加速因子加速
        /// 模仿 macOS 触控板的跟手体验，支持可配置的加速度
        /// </summary>
        /// <param name="velocity">速度大小（像素/秒）</param>
        /// <returns>速度倍数</returns>
        private double CalculateSpeedMultiplier(double velocity)
        {
            double absVelocity = Math.Abs(velocity);

            if (absVelocity < 100)
            {
                // 极慢速：大幅放大，确保能滚动
                return 5.0;
            }
            else if (absVelocity < 500)
            {
                // 慢速：从 5.0 平滑过渡到 2.0
                double t = (absVelocity - 100) / 400.0;
                return 5.0 - 3.0 * t;
            }
            else if (absVelocity < 2000)
            {
                // 中速：从 2.0 平滑过渡到 1.0
                double t = (absVelocity - 500) / 1500.0;
                return 2.0 - 1.0 * t;
            }
            else
            {
                // 快速：根据加速因子调整
                // AccelerationFactor = 1.0 时，保持 1.0 倍数（跟手，不加速）
                // AccelerationFactor > 1.0 时，适度加速
                // AccelerationFactor < 1.0 时，抑制速度
                // 使用温和的加速曲线：倍数 = 1.0 * AccelerationFactor^(log(velocity/2000))
                // 这样在 2000 px/s 时倍数恰好为 1.0，更快时根据 AccelerationFactor 缓慢增长
                double velocityRatio = absVelocity / 2000.0;  // >= 1.0
                double exponent = Math.Log(velocityRatio, 2.0) * 0.3;  // 温和的对数增长
                return Math.Pow(_settings.AccelerationFactor, exponent);
            }
        }

        /// <summary>
        /// 检测窗口是否为 WinUI/UWP 应用
        /// </summary>
        private bool IsWinUIOrUWPApp(ManagedWinapi.Windows.SystemWindow window)
        {
            if (window == null)
                return false;

            try
            {
                // 获取顶级窗口（向上遍历到没有父窗口的窗口）
                var topWindow = window;
                while (topWindow.Parent != null && topWindow.Parent.HWnd != IntPtr.Zero)
                {
                    topWindow = topWindow.Parent;
                }

                string className = topWindow.ClassName;

                // UWP/WinUI 应用的标准窗口类
                if ("ApplicationFrameWindow".Equals(className, StringComparison.Ordinal) ||
                    "Windows.UI.Core.CoreWindow".Equals(className, StringComparison.Ordinal) ||
                    "WinUIDesktopWin32WindowClass".Equals(className, StringComparison.Ordinal))
                {
                    Logging.LogDebug($"[InertialScroll] WinUI detected by class: {className}");
                    return true;
                }

                // WinUI 3 桌面应用通过进程名检测
                try
                {
                    string processName = topWindow.Process?.ProcessName;
                    if (string.IsNullOrEmpty(processName))
                        return false;

                    // 常见 WinUI/UWP 应用进程名
                    string[] winUIProcesses = {
                        "SystemSettings",      // Windows 设置
                        "PowerToys.Settings",  // PowerToys 设置
                        "WinStore.App",        // Microsoft Store
                        "PhoneExperienceHost", // 手机连接
                        "WindowsTerminal",     // Windows Terminal (WinUI)
                        "DevHome",             // Dev Home
                        "ms-teams",            // Microsoft Teams (new)
                    };

                    foreach (var name in winUIProcesses)
                    {
                        if (processName.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            Logging.LogDebug($"[InertialScroll] WinUI detected by process: {processName}");
                            return true;
                        }
                    }
                }
                catch
                {
                    // 进程访问被拒绝或已退出
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}