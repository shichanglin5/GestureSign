using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using WindowsInput;
using GestureSign.Common.Localization;
using GestureSign.Common.Plugins;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;

namespace GestureSign.CorePlugins.InertialScroll
{
    /// <summary>
    /// 惯性滚动插件 - 基于双指滑动速度实现自然的惯性滚动效果
    /// </summary>
    public class InertialScrollPlugin : IPlugin
    {
        #region Win32 API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x01000;
        private const int WHEEL_DELTA = 120;

        #endregion

        #region Private Variables

        private InertialScrollSettings _settings = null;
        private InertialScrollUI _gui = null;
        private System.Threading.CancellationTokenSource _cancellationTokenSource = null;

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
            Logging.LogDebug($"[InertialScrollPlugin] Gestured called: Velocity={(actionPoint.Velocity?.Magnitude ?? 0):F1} px/s");

            if (_settings == null)
            {
                Logging.LogWarning("[InertialScrollPlugin] Settings is null");
                return false;
            }

            // 取消之前的惯性滚动（如果有）
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new System.Threading.CancellationTokenSource();

            // 检查是否有速度信息
            if (actionPoint.Velocity == null)
            {
                Logging.LogWarning("[InertialScrollPlugin] No velocity information available");
                return false;
            }

            var velocity = actionPoint.Velocity.Value;

            // 检查速度是否显著
            if (!velocity.IsSignificant(_settings.MinimumVelocity))
            {
                Logging.LogDebug($"[InertialScrollPlugin] Velocity too low: {velocity.Magnitude:F1} < {_settings.MinimumVelocity}");
                // 速度太低,执行简单滚动
                ExecuteSimpleScroll(velocity);
                return false;
            }

            Logging.LogInfo($"[InertialScrollPlugin] Executing inertial scroll: Velocity={velocity.Magnitude:F1} px/s, Direction={_settings.Direction}, Inertia={_settings.EnableInertia}");

            // 应用方向过滤
            if (_settings.Direction == ScrollDirection.Vertical)
                velocity = new VelocityVector(0, velocity.VelocityY);
            else if (_settings.Direction == ScrollDirection.Horizontal)
                velocity = new VelocityVector(velocity.VelocityX, 0);

            // 应用倍数和反向
            double multiplier = _settings.DistanceMultiplier * (_settings.ReverseDirection ? -1 : 1);
            velocity = new VelocityVector(
                velocity.VelocityX * multiplier,
                velocity.VelocityY * multiplier
            );

            if (_settings.EnableInertia)
            {
                // 异步执行惯性滚动,不阻塞主线程
                var token = _cancellationTokenSource.Token;
                _ = Task.Run(() => SimulateInertialScroll(velocity, _settings, token), token);
            }
            else
            {
                // 简单一次性滚动
                ExecuteSimpleScroll(velocity);
            }

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
        /// 模拟惯性滚动效果
        /// </summary>
        private async Task SimulateInertialScroll(VelocityVector initialVelocity, InertialScrollSettings settings, System.Threading.CancellationToken cancellationToken)
        {
            const int frameInterval = 16;  // 60 FPS (16ms/帧)

            // 应用初始强度
            double velocityX = initialVelocity.VelocityX * settings.InertiaStrength;
            double velocityY = initialVelocity.VelocityY * settings.InertiaStrength;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                while (stopwatch.Elapsed.TotalSeconds < settings.InertiaDuration && !cancellationToken.IsCancellationRequested)
                {
                    // 计算当前速度大小
                    double magnitude = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
                    if (magnitude < settings.MinimumVelocity)
                        break;  // 低于阈值,提前终止

                    // 计算本帧的滚动距离
                    double frameVelocityX = velocityX * frameInterval / 1000.0;  // 像素
                    double frameVelocityY = velocityY * frameInterval / 1000.0;

                    // 转换为滚动单位 (默认 5 像素 = 1 滚动单位，可在 UI 配置)
                    int scrollX = (int)Math.Round(frameVelocityX / settings.PixelsPerScrollUnit);
                    int scrollY = (int)Math.Round(frameVelocityY / settings.PixelsPerScrollUnit);

                    // 执行滚动 - 使用 SendInput API 注入硬件级输入
                    if (scrollY != 0)
                        SendScrollMessage(scrollY, isHorizontal: false);
                    if (scrollX != 0)
                        SendScrollMessage(scrollX, isHorizontal: true);

                    // 应用指数衰减
                    velocityX *= settings.DecayRate;
                    velocityY *= settings.DecayRate;

                    await Task.Delay(frameInterval, cancellationToken);
                }
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                // 被取消,正常退出
                Logging.LogDebug("[InertialScrollPlugin] Inertial scroll cancelled");
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        /// <summary>
        /// 执行简单的一次性滚动
        /// </summary>
        private void ExecuteSimpleScroll(VelocityVector velocity)
        {
            try
            {
                int scrollX = (int)Math.Round(velocity.VelocityX / _settings.PixelsPerScrollUnit);
                int scrollY = (int)Math.Round(velocity.VelocityY / _settings.PixelsPerScrollUnit);

                if (scrollY != 0)
                    SendScrollMessage(scrollY, isHorizontal: false);
                if (scrollX != 0)
                    SendScrollMessage(scrollX, isHorizontal: true);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        /// <summary>
        /// 发送滚动消息 - 使用 SendInput API 注入硬件级输入 (与 Windows 原生滚动完全一致)
        /// </summary>
        private static void SendScrollMessage(int delta, bool isHorizontal)
        {
            try
            {
                // 获取前台窗口信息用于诊断
                IntPtr foregroundWindow = GetForegroundWindow();
                string windowInfo = "Unknown";
                string className = "Unknown";

                if (foregroundWindow != IntPtr.Zero)
                {
                    try
                    {
                        var titleBuilder = new System.Text.StringBuilder(256);
                        var classBuilder = new System.Text.StringBuilder(256);
                        GetWindowText(foregroundWindow, titleBuilder, 256);
                        GetClassName(foregroundWindow, classBuilder, 256);

                        GetWindowThreadProcessId(foregroundWindow, out int pid);
                        var process = System.Diagnostics.Process.GetProcessById(pid);
                        windowInfo = $"{process.ProcessName} - {titleBuilder}";
                        className = classBuilder.ToString();
                    }
                    catch
                    {
                        // Ignore errors getting window info
                    }
                }

                // 创建 INPUT 结构
                var input = new INPUT
                {
                    type = INPUT_MOUSE,
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = (uint)(delta * WHEEL_DELTA),
                        dwFlags = isHorizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                };

                // 使用 SendInput 注入鼠标滚轮事件 (硬件级输入，所有应用都会响应)
                uint result = SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));

                if (result == 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Logging.LogWarning($"[InertialScrollPlugin] SendInput failed with error code: {errorCode}, " +
                                      $"ForegroundWindow: {windowInfo}, ClassName: {className}, " +
                                      $"Direction: {(isHorizontal ? "Horizontal" : "Vertical")}, Delta: {delta}");
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
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
                return "Inertial Scroll";

            string direction = _settings.Direction switch
            {
                ScrollDirection.Vertical => "Vertical",
                ScrollDirection.Horizontal => "Horizontal",
                _ => "Auto"
            };

            string inertia = _settings.EnableInertia ? $"Inertia:{_settings.InertiaStrength:F1}" : "No Inertia";

            return $"Inertial Scroll ({direction}, {inertia})";
        }

        #endregion
    }
}
