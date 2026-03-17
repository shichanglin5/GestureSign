using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;

namespace GestureSign.Common.Plugins
{
    public class PluginManager : IPluginManager
    {
        #region Private Variables

        // Create variable to hold the only allowed instance of this class
        static readonly PluginManager _Instance = new PluginManager();
        List<IPluginInfo> _Plugins = new List<IPluginInfo>();
        private Task _lastActionTask;
        private SynchronizationContext _mainContext;

        #endregion

        #region Public Properties

        public IPluginInfo[] Plugins { get { return _Plugins.ToArray(); } }

        public static PluginManager Instance
        {
            get { return _Instance; }
        }

        #endregion

        #region Constructors

        protected PluginManager()
        {

        }

        #endregion

        #region Events

        protected void PointCapture_GestureRecognized(object sender, RecognitionEventArgs e)
        {
            var pointCapture = (IPointCapture)sender;
            // Get action to be executed
            var executableActions = ApplicationManager.Instance.GetRecognizedDefinedAction(e.GestureId)?.ToList();

            if (executableActions == null || executableActions.Count == 0)
            {
                return;
            }
            ExecuteAction(executableActions, pointCapture.Mode, pointCapture.SourceDevice, e.ContactIdentifiers, e.FirstCapturedPoints, e.Points);
        }

        #endregion

        #region Public Methods

        public void ExecuteAction(List<IAction> executableActions, CaptureMode mode, Devices devices, List<int> contactIdentifiers, List<Point> firstCapturedPoints, List<List<Point>> points, VelocityVector? velocity = null)
        {
            // Exit if we're teaching
            if (mode == CaptureMode.Training)
                return;

            SystemWindow? target;

            if (Configuration.AppConfig.ReFetchTargetWindowOnExecution)
            {
                // 重新获取目标窗口
                var targetMode = devices == Input.Devices.TouchPad
                    ? Configuration.AppConfig.TouchPadWindowTargetMode
                    : Configuration.AppConfig.TouchScreenWindowTargetMode;

                target = null;

                switch (targetMode)
                {
                    case Input.WindowTargetMode.MousePosition:
                        var mousePosition = System.Windows.Forms.Cursor.Position;
                        target = ApplicationManager.Instance.GetWindowFromPoint(mousePosition);
                        break;

                    case Input.WindowTargetMode.ActiveWindow:
                        target = SystemWindow.ForegroundWindow;
                        // 如果 foreground 窗口已最小化，根据设备类型回退
                        if (target != null && target.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                        {
                            if ((devices & Devices.TouchPad) != 0)
                            {
                                var mousePos = System.Windows.Forms.Cursor.Position;
                                target = ApplicationManager.Instance.GetWindowFromPoint(mousePos);
                            }
                            else
                            {
                                target = ApplicationManager.Instance.GetWindowFromPoint(firstCapturedPoints.FirstOrDefault());
                            }
                        }
                        break;

                    case Input.WindowTargetMode.GestureStartPosition:
                        target = ApplicationManager.Instance.GetWindowFromPoint(firstCapturedPoints.FirstOrDefault());
                        break;
                }

                // Fallback to gesture start position window if target is null
                if (target == null)
                    target = ApplicationManager.Instance.GetWindowFromPoint(firstCapturedPoints.FirstOrDefault());
            }
            else
            {
                // 复用匹配阶段获取的窗口
                target = ApplicationManager.Instance.CaptureWindow;
            }

            var pointInfo = new PointInfo(firstCapturedPoints, points, target, _mainContext, velocity);
            var action = new Action<object>(o =>
            {
                foreach (IAction executableAction in executableActions)
                {
                    // Exit if there is no action configured
                    if (executableAction == null || (executableAction.IgnoredDevices & devices) != 0 ||
                    executableAction.Commands == null || !Compute(executableAction.Condition, points, contactIdentifiers))
                        continue;

                    var commandList = executableAction.Commands.Where(command => command != null && command.IsEnabled).ToList();
                    foreach (var command in commandList)
                    {
                        if (mode == CaptureMode.UserDisabled && !"GestureSign.CorePlugins.ToggleDisableGestures".Equals(command.PluginClass))
                            continue;

                        // Skip WaitForIdle for window switching plugins to prevent keyboard state issues
                        // WaitForIdle can cause race conditions when sending Alt+Tab while window is still processing messages
                        // target.WaitForIdle(200);

                        // Locate the plugin associated with this action
                        IPluginInfo pluginInfo = FindPluginByClassAndFilename(command.PluginClass, command.PluginFilename);

                        // Exit if there is no plugin available for action
                        if (pluginInfo == null)
                        {
                            Logging.LogWarning($"[PluginManager] Plugin not found: {command.PluginClass} ({command.PluginFilename}), action skipped");
                            continue;
                        }

                        if (commandList.IndexOf(command) == 0)
                        {
                            if (executableAction.ActivateWindow == null && pluginInfo.Plugin.ActivateWindowDefault ||
                            executableAction.ActivateWindow.GetValueOrDefault())
                            {
                                SystemWindow? windowToActivate;

                                if (Configuration.AppConfig.ReFetchTargetWindowOnExecution)
                                {
                                    var targetMode = devices == Input.Devices.TouchPad
                                        ? Configuration.AppConfig.TouchPadWindowTargetMode
                                        : Configuration.AppConfig.TouchScreenWindowTargetMode;

                                    windowToActivate = null;

                                    switch (targetMode)
                                    {
                                        case Input.WindowTargetMode.MousePosition:
                                            var mousePosition = System.Windows.Forms.Cursor.Position;
                                            windowToActivate = ApplicationManager.Instance.GetWindowFromPoint(mousePosition);
                                            break;

                                        case Input.WindowTargetMode.ActiveWindow:
                                            windowToActivate = SystemWindow.ForegroundWindow;
                                            if (windowToActivate != null && windowToActivate.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                                            {
                                                if ((devices & Devices.TouchPad) != 0)
                                                    windowToActivate = ApplicationManager.Instance.GetWindowFromPoint(System.Windows.Forms.Cursor.Position);
                                                else
                                                    windowToActivate = ApplicationManager.Instance.GetWindowFromPoint(firstCapturedPoints.FirstOrDefault());
                                            }
                                            break;

                                        case Input.WindowTargetMode.GestureStartPosition:
                                            windowToActivate = ApplicationManager.Instance.GetWindowFromPoint(firstCapturedPoints.FirstOrDefault());
                                            break;
                                    }
                                }
                                else
                                {
                                    windowToActivate = target;
                                }

                                if (windowToActivate != null && windowToActivate.HWnd.ToInt64() != SystemWindow.ForegroundWindow?.HWnd.ToInt64())
                                    SystemWindow.ForegroundWindow = windowToActivate;
                            }
                        }

                        pluginInfo.Plugin.Deserialize(command.CommandSettings);
                        if (Logging.CurrentLogLevel >= LogLevel.Debug)
                        {
                            var fgWin = SystemWindow.ForegroundWindow;
                            var windowInfo = GetWindowInfo(fgWin);
                            var pluginName = pluginInfo.Plugin.Name;
                            var pluginDescription = pluginInfo.Plugin.Description ?? pluginName;
                            Logging.LogDebug($"[PluginManager] Executing: Action={pluginName} -> {pluginDescription}, ForegroundWindow=0x{fgWin?.HWnd:X} '{fgWin?.Title}'{windowInfo}");
                        }
                        // Execute plugin process
                        pluginInfo.Plugin.Gestured(pointInfo);
                    }
                }
            });

            var observeExceptions = new Action<Task>(t =>
            {
                Logging.LogException(t.Exception.InnerException);
            });

            if (_lastActionTask == null)
            {
                _lastActionTask = Task.Factory.StartNew(action, null);
                _lastActionTask.ContinueWith(observeExceptions, TaskContinuationOptions.OnlyOnFaulted);
            }
            else
            {
                _lastActionTask = _lastActionTask.ContinueWith(action);
                _lastActionTask.ContinueWith(observeExceptions, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        /// <summary>
        /// 执行命令列表（供连续手势 Custom 模式使用）
        /// 复用任务队列保证串行执行，并检查 UserDisabled 模式
        /// </summary>
        public void ExecuteCommands(IEnumerable<ICommand> commands, PointInfo pointInfo, CaptureMode mode)
        {
            if (commands == null || !commands.Any())
                return;

            // 训练模式下不执行
            if (mode == CaptureMode.Training)
                return;

            var action = new Action<object>(o =>
            {
                foreach (var command in commands)
                {
                    if (command == null || !command.IsEnabled)
                        continue;

                    // 禁用模式下只允许 ToggleDisableGestures
                    if (mode == CaptureMode.UserDisabled && !"GestureSign.CorePlugins.ToggleDisableGestures".Equals(command.PluginClass))
                        continue;

                    IPluginInfo pluginInfo = FindPluginByClassAndFilename(command.PluginClass, command.PluginFilename);
                    if (pluginInfo == null)
                    {
                        Logging.LogWarning($"[PluginManager] Plugin not found: {command.PluginClass} ({command.PluginFilename}), action skipped");
                        continue;
                    }

                    pluginInfo.Plugin.Deserialize(command.CommandSettings);
                    pluginInfo.Plugin.Gestured(pointInfo);
                }
            });

            var observeExceptions = new Action<Task>(t =>
            {
                Logging.LogException(t.Exception.InnerException);
            });

            if (_lastActionTask == null)
            {
                _lastActionTask = Task.Factory.StartNew(action, null);
                _lastActionTask.ContinueWith(observeExceptions, TaskContinuationOptions.OnlyOnFaulted);
            }
            else
            {
                _lastActionTask = _lastActionTask.ContinueWith(action);
                _lastActionTask.ContinueWith(observeExceptions, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        public bool LoadPlugins(IHostControl host)
        {
            // Default return value to failure
            bool bFailed = true;

            // Clear any existing plugins
            _Plugins = new List<IPluginInfo>();
            //_Plugins.Clear();
            string? directoryPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (directoryPath == null) return true;

            // Load core plugins.
            string corePluginsPath = Path.Combine(directoryPath, "GestureSign.CorePlugins.dll");
            if (File.Exists(corePluginsPath))
            {
                _Plugins.AddRange(LoadPluginsFromAssembly(corePluginsPath, host));
                bFailed = false;
            }

            var extraPluginsPath = Path.Combine(directoryPath, "Plugins");
            if (Directory.Exists(extraPluginsPath))
            {
                // Load extra plugins.
                foreach (string sFilePath in Directory.GetFiles(extraPluginsPath, "*.dll"))
                {
                    _Plugins.AddRange(LoadPluginsFromAssembly(sFilePath, host));
                    bFailed = false;
                }
            }

            return bFailed;
        }

        public IPluginInfo FindPluginByClassAndFilename(string PluginClass, string PluginFilename)
        {
            // Get reference to plugin using PluginClass and PluginFilename
            return _Plugins.FirstOrDefault(p => p.Class == PluginClass && p.Filename == PluginFilename);
        }

        public bool PluginExists(string PluginClass, string PluginFilename)
        {
            return _Plugins.Exists(p => p.Class == PluginClass && p.Filename == PluginFilename);
        }

        #endregion

        #region Private Methods

        private string GetWindowInfo(SystemWindow window)
        {
            if (window == null)
                return string.Empty;

            try
            {
                var hWnd = window.HWnd;
                var className = window.ClassName;
                var aumid = WindowMatcher.GetWindowAUMID(hWnd);
                var processPath = WindowMatcher.GetProcessPath(hWnd, out string processName);
                var process = processPath ?? processName;

                return $" (aumid={aumid ?? "null"}, class={className}, process={process})";
            }
            catch
            {
                return string.Empty;
            }
        }

        private List<IPluginInfo> LoadPluginsFromAssembly(string assemblyLocation, IHostControl hostControl)
        {
            List<IPluginInfo> retPlugins = new List<IPluginInfo>();

            try
            {
                //To avoid exception System.NotSupportedException
                byte[] file = File.ReadAllBytes(assemblyLocation);
                Assembly aPlugin = Assembly.Load(file);

                Localization.LocalizationProvider.Instance.AddAssembly(aPlugin.FullName);

                Type[] tPluginTypes = aPlugin.GetTypes();

                foreach (Type tPluginType in tPluginTypes)
                    if (tPluginType.GetInterface("IPlugin") != null)
                    {
                        try
                        {
                            IPlugin plugin = Activator.CreateInstance(tPluginType) as IPlugin;

                            // If we have a new instance of a plugin, initialize it and add it to return list
                            if (plugin != null)
                            {
                                plugin.HostControl = hostControl;
                                plugin.Initialize();
                                retPlugins.Add(new PluginInfo(plugin, tPluginType.FullName, Path.GetFileName(assemblyLocation)));
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.LogException(new Exception($"Failed to load plugin type {tPluginType.FullName}", ex));
                        }
                    }
            }
            catch (Exception ex)
            {
                Logging.LogException(new Exception($"Failed to load assembly {assemblyLocation}", ex));
            }

            return retPlugins;
        }

        private bool Compute(string condition, List<List<Point>> pointList, List<int> contactIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(condition)) return true;

            string expression = GetExpression(condition, pointList, contactIdentifiers);
            try
            {
                DataTable dataTable = new DataTable();
                var result = dataTable.Compute(expression, null);
                return result is DBNull || Convert.ToBoolean(result);
            }
            catch (EvaluateException)
            {
                return false;
            }
        }

        private string GetExpression(string condition, List<List<Point>> pointList, List<int> contactIdentifiers)
        {
            for (int i = 1; i <= pointList.Count; i++)
            {
                int startX = pointList[i - 1].FirstOrDefault().X;
                int startY = pointList[i - 1].FirstOrDefault().Y;
                int endX = pointList[i - 1].LastOrDefault().X;
                int endY = pointList[i - 1].LastOrDefault().Y;

                if (condition.Contains('%'))
                {
                    int width = (int)System.Windows.SystemParameters.VirtualScreenWidth;
                    int height = (int)System.Windows.SystemParameters.VirtualScreenHeight;
                    condition = ReplaceVariables(condition, i, "start_X%", startX * 100 / width);
                    condition = ReplaceVariables(condition, i, "start_Y%", startY * 100 / height);
                    condition = ReplaceVariables(condition, i, "end_X%", endX * 100 / width);
                    condition = ReplaceVariables(condition, i, "end_Y%", endY * 100 / height);
                }

                condition = ReplaceVariables(condition, i, "start_X", startX);
                condition = ReplaceVariables(condition, i, "start_Y", startY);
                condition = ReplaceVariables(condition, i, "end_X", endX);
                condition = ReplaceVariables(condition, i, "end_Y", endY);

                condition = ReplaceVariables(condition, i, "ID", contactIdentifiers[i - 1]);
            }
            return condition;
        }

        private string ReplaceVariables(string str, int id, string key, int value)
        {
            string variable = $"finger_{id}_{key}";
            return str.Replace(variable, value.ToString());
        }

        #endregion

        #region ILoadable Methods

        public void Load(IHostControl host, SynchronizationContext syncContext = null)
        {
            _mainContext = syncContext;
            // Create empty list of plugins, then load as many as possible from plugin directory
            LoadPlugins(host);

            if (host == null) return;
            host.PointCapture.GestureRecognized += PointCapture_GestureRecognized;
        }

        #endregion
    }
}

