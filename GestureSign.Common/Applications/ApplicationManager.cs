using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;

namespace GestureSign.Common.Applications
{
    public class ApplicationManager : IApplicationManager, INotifyCollectionChanged
    {
        #region Private Variables

        // Create thread-safe lazy singleton instance
        private static readonly Lazy<ApplicationManager> _instance = new Lazy<ApplicationManager>(() => new ApplicationManager());
        private List<IApplication> _applications;
        IEnumerable<IApplication> _recognizedApplication;
        private Timer _timer;
        private Point _lastTouchPadGestureMousePosition;
        #endregion

        #region Public Instance Properties

        public SystemWindow CaptureWindow { get; private set; }
        public IEnumerable<IApplication> RecognizedApplication { get { return _recognizedApplication; } }

        public List<IApplication> Applications
        {
            get
            {
                if (LoadingTask.IsCompleted)
                    return _applications != null ? _applications : _applications = new List<IApplication>();
                else
                    return new List<IApplication>();
            }
        }

        public static ApplicationManager Instance
        {
            get { return _instance.Value; }
        }

        public Task LoadingTask { get; }

        #endregion

        #region Constructors

        protected ApplicationManager()
        {
            // Load applications from disk, if file couldn't be loaded, create an empty applications list
            LoadingTask = LoadApplications();
        }

        #endregion

        #region Events

        protected void PointCapture_CaptureStarted(object sender, PointsCapturedEventArgs e)
        {
            var pointCapture = (IPointCapture)sender;
            if (pointCapture.Mode == CaptureMode.Training) return;

            // 单指手势不需要识别，直接跳过
            if (e.FingerCount < 2) return;

            CaptureWindow = GetCaptureWindowByTargetMode(pointCapture.SourceDevice, e.FirstCapturedPoints.FirstOrDefault());
            _recognizedApplication = GetApplicationFromWindow(CaptureWindow);

            int maxThreshold = 0, maxLimitNumber = 1;
            foreach (IApplication app in _recognizedApplication)
            {
                switch (app)
                {
                    case GlobalApp a:
                        maxLimitNumber = a.LimitNumberOfFingers > maxLimitNumber ? a.LimitNumberOfFingers : maxLimitNumber;
                        if (AppConfig.IgnoreFullScreen && IsFullScreenWindow(e.FirstCapturedPoints.FirstOrDefault()))
                        {
                            e.Cancel = true;
                            return;
                        }
                        break;
                    case UserApp a:
                        maxThreshold = a.BlockTouchInputThreshold > maxThreshold ? a.BlockTouchInputThreshold : maxThreshold;
                        maxLimitNumber = a.LimitNumberOfFingers > maxLimitNumber ? a.LimitNumberOfFingers : maxLimitNumber;
                        break;
                    case IgnoredApp a:
                        if (a.IsEnabled)
                        {
                            e.Cancel = true;
                            return;
                        }
                        break;
                    default:
                        return;
                }
            }

            bool isTouchDevice = (pointCapture.SourceDevice & Devices.TouchDevice) != 0;
            // Use FingerCount (total fingers) instead of Points.Count (feature finger count)
            int actualFingerCount = e.FingerCount > 0 ? e.FingerCount : e.Points.Count;
            bool fingersLessThanLimit = actualFingerCount < maxLimitNumber;
            e.Cancel = isTouchDevice && fingersLessThanLimit;

            Log.Logging.LogTrace($"[ApplicationManager] Cancel calculation: isTouchDevice={isTouchDevice}, FingerCount={e.FingerCount}, actualFingerCount={actualFingerCount}, maxLimitNumber={maxLimitNumber}, fingersLessThanLimit={fingersLessThanLimit}, Cancel={e.Cancel}");

            e.BlockTouchInputThreshold = maxThreshold;
        }

        protected void PointCapture_BeforePointsCaptured(object sender, PointsCapturedEventArgs e)
        {
        }

        #endregion

        #region Custom Events

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public static event EventHandler ApplicationSaved;
        public static event EventHandler OnLoadApplicationsCompleted;

        #endregion

        #region Public Methods

        public void Load(IPointCapture pointCapture)
        {
            // Shortcut method to control singleton instantiation
            // Consume Point Capture events
            if (pointCapture != null)
            {
                pointCapture.CaptureStarted += new PointsCapturedEventHandler(PointCapture_CaptureStarted);
                pointCapture.BeforePointsCaptured += new PointsCapturedEventHandler(PointCapture_BeforePointsCaptured);
            }
        }

        public void AddApplication(IApplication application)
        {
            Applications.Add(application);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, application));
        }

        public void AddApplicationRange(List<IApplication> applications)
        {
            Applications.AddRange(applications);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, applications));
        }

        public void RemoveApplication(IApplication application)
        {
            Applications.Remove(application);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, application));
        }

        public void ReplaceApplication(IApplication oldApplication, IApplication newApplication)
        {
            Applications.Remove(oldApplication);
            Applications.Add(newApplication);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newApplication, oldApplication));
        }

        public void RemoveAllApplication()
        {
            Applications.Clear();
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void RemoveIgnoredApplications(string applicationName)
        {
            Applications.RemoveAll(app => app is IgnoredApp && app.Name == applicationName);
        }

        public bool SaveApplications()
        {
            TrimActions(Applications);

            if (_timer == null)
            {
                _timer = new Timer(new TimerCallback(SaveFile), true, 200, Timeout.Infinite);
            }
            else _timer.Change(200, Timeout.Infinite);
            return true;
        }

        private void SaveFile(object state)
        {
            // Save application list
            bool flag = FileManager.SaveObject(
                 Applications, Path.Combine(AppConfig.ApplicationDataPath, Constants.ActionFileName), true);
            if (flag) { ApplicationSaved.Invoke(this, EventArgs.Empty); }

        }

        public Task LoadApplications()
        {
            Action<bool> loadCompleted =
                result =>
                {
                    if (!result)
                        if (!LoadBackup())
                            if (!LoadLegacy())
                                if (!LoadDefaults())
                                    _applications = new List<IApplication>();
                    OnLoadApplicationsCompleted?.Invoke(this, EventArgs.Empty);
                };

            return Task.Run(() =>
            {
                // Load application list from file
                _applications =
                    FileManager.LoadObject<List<IApplication>>(
                        Path.Combine(AppConfig.ApplicationDataPath, Constants.ActionFileName), true, true);
                return _applications != null;
            }).ContinueWith(antecendent => loadCompleted(antecendent.Result));
        }

        private bool LoadDefaults()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Defaults", Constants.ActionFileName);

            _applications = FileManager.LoadObject<List<IApplication>>(path, false, true);
            // Ensure we got an object back
            if (_applications == null)
                return false; // No object, failed

            return true; // Success
        }

        private bool LoadBackup()
        {
            var directory = new DirectoryInfo(AppConfig.BackupPath);
            if (directory.Exists)
            {
                var actionfiles = directory.EnumerateFiles("*" + Constants.ActionExtension).OrderByDescending(f => f.LastWriteTime);
                foreach (var file in actionfiles)
                {
                    _applications = FileManager.LoadObject<List<IApplication>>(file.FullName, false, true);
                    if (_applications != null) return true;
                }
            }
            return false;
        }

        public SystemWindow GetWindowFromPoint(Point point)
        {
            return SystemWindow.FromPointEx(point.X, point.Y, true, true);
        }

        public IApplication[] GetApplicationFromWindow(SystemWindow window, bool userApplicationOnly = false)
        {
            if (Applications == null || window == null)
            {
                return new[] { GetGlobalApplication() };
            }

            string className, title, fileName;
            GetWindowInfo(window, out className, out title, out fileName);

            IApplication[] definedApplications = userApplicationOnly
                ? FindMatchApplications(Applications.Where(a => a is UserApp), window, className, title, fileName)
                : FindMatchApplications(Applications.Where(a => !(a is GlobalApp)), window, className, title, fileName);
            // Try to find any user or ignored applications that match the given system window
            // If not user or ignored application could be found, return the global application
            return definedApplications.Length != 0
                ? definedApplications
                : userApplicationOnly ? new IApplication[0] : new IApplication[] { GetGlobalApplication() };
        }

        public IEnumerable<IApplication> GetApplicationFromPoint(Point testPoint)
        {
            var systemWindow = GetWindowFromPoint(testPoint);
            return GetApplicationFromWindow(systemWindow);
        }

        public IEnumerable<IAction> GetRecognizedDefinedAction(string GestureName)
        {
            return GetDefinedAction(GestureName, _recognizedApplication, true);
        }

        public List<IAction> GetRecognizedDefinedAction(Func<IAction, bool> predicate)
        {
            if (_recognizedApplication == null)
            {
                return new List<IAction>();
            }
            var recognizedActions = _recognizedApplication.Where(app => !(app is IgnoredApp) && app.Actions != null).SelectMany(app => app.Actions).Where(a => predicate(a)).ToList();
            // If there is was no action found on given application, try to get an action for global application
            if (recognizedActions.Count == 0)
                recognizedActions = GetGlobalApplication().Actions.Where(a => predicate(a)).ToList();

            return recognizedActions;
        }

        public IEnumerable<IAction> GetDefinedAction(string gestureName, IEnumerable<IApplication> application, bool useGlobal)
        {
            if (application == null)
            {
                return Enumerable.Empty<IAction>();
            }
            // Attempt to retrieve an action on the application passed in
            IEnumerable<IAction> finalAction =
                application.Where(app => !(app is IgnoredApp) && app.Actions != null).SelectMany(app => app.Actions.Where(a => a.GestureName == gestureName && a.Commands != null && a.Commands.Any(com => com != null && com.IsEnabled)));
            // If there is was no action found on given application, try to get an action for global application
            if (!finalAction.Any() && useGlobal)
                finalAction = GetGlobalApplication().Actions.Where(a => a.GestureName == gestureName);

            // Return whatever the result was
            return finalAction;
        }

        public IApplication GetExistingUserApplication(string ApplicationName)
        {
            return Applications.FirstOrDefault(a => a is UserApp && a.Name == ApplicationName.Trim());
        }

        public bool IsGlobalAction(string ActionName)
        {
            return Applications.Exists(a => a is GlobalApp && a.Actions.Any(ac => ac.Name == ActionName.Trim()));
        }

        public bool ApplicationExists(string ApplicationName)
        {
            return Applications.Exists(a => a.Name == ApplicationName.Trim());
        }

        public IApplication[] GetAvailableUserApplications()
        {
            return Applications.Where(a => a is UserApp).OrderBy(a => a.Name).ToArray();
        }

        public IEnumerable<IgnoredApp> GetIgnoredApplications()
        {
            return Applications.Where(a => a is IgnoredApp).OrderBy(a => a.Name).Cast<IgnoredApp>();
        }

        public IApplication GetGlobalApplication()
        {
            var apps = Applications;
            GlobalApp globalApp = apps.FirstOrDefault(a => a is GlobalApp) as GlobalApp;
            if (globalApp == null)
            {
                globalApp = new GlobalApp() { Group = String.Empty };
                apps.Add(globalApp);
                return globalApp;
            }
            else return globalApp;
        }

        public IApplication[] FindMatchApplications<TApplication>(List<MatchCondition> matchConditions, string excludedApplication = null) where TApplication : IApplication
        {
            if (matchConditions == null || matchConditions.Count == 0)
                return Array.Empty<IApplication>();

            return Applications.FindAll(
                    a => a is TApplication &&
                        a.MatchConditions != null &&
                        a.MatchConditions.Count == matchConditions.Count &&
                        matchConditions.All(mc => a.MatchConditions.Any(amc =>
                            amc.Type == mc.Type &&
                            string.Equals(amc.Value, mc.Value, StringComparison.OrdinalIgnoreCase))) &&
                        excludedApplication != a.Name).ToArray();
        }

        public SystemWindow GetForegroundApplications()
        {
            CaptureWindow = SystemWindow.ForegroundWindow;
            _recognizedApplication = GetApplicationFromWindow(CaptureWindow);
            return CaptureWindow;
        }

        public IApplication AddApplication<TApp>(TApp app, string executablefilePath) where TApp : IApplication
        {
            var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablefilePath);
            app.Name = string.IsNullOrWhiteSpace(versionInfo.ProductName) ? Path.GetFileNameWithoutExtension(executablefilePath) : versionInfo.ProductName;
            app.MatchConditions = new List<MatchCondition>
            {
                new MatchCondition
                {
                    Type = MatchConditionType.ProcessName,
                    Value = Path.GetFileName(executablefilePath)
                }
            };

            var matchApplications = FindMatchApplications<TApp>(app.MatchConditions);
            if (matchApplications.Length != 0)
            {
                return matchApplications[0];
            }
            var existingApp = Applications.Find(a => a.Name == app.Name && a is TApp);
            if (existingApp != null)
            {
                return existingApp;
            }
            AddApplication(app);
            SaveApplications();
            return app;
        }

        public static SystemWindow GetRealWindow(SystemWindow window)
        {
            try
            {
                if (VersionHelper.IsWindows10OrGreater() && "ApplicationFrameWindow".Equals(window.ClassName))
                {
                    var realWindow = window.AllChildWindows.FirstOrDefault(w => "Windows.UI.Core.CoreWindow".Equals(w.ClassName));
                    if (realWindow != null)
                        return realWindow;
                }
                return window;
            }
            catch (Exception)
            {
                return window;
            }
        }

        public static SystemWindow GetWindowInfo(SystemWindow window, out string className, out string title, out string fileName)
        {
            var realWindow = GetRealWindow(window);
            className = fileName = null;

            if (VersionHelper.OsVersion >= new Version(10, 0, 17134))
            {
                title = window.Title;
            }
            else
                title = realWindow.Title;

            try
            {
                className = realWindow.ClassName;
            }
            catch (Exception ex)
            {
                // Expected: window may be closed or inaccessible
                Logging.LogTrace($"[ApplicationManager] Failed to get window class name: {ex.Message}");
            }

            try
            {
                fileName = Path.GetFileName(realWindow.GetProcessFilePath());
            }
            catch (Exception ex)
            {
                // Expected: process may be inaccessible or terminated
                Logging.LogTrace($"[ApplicationManager] Failed to get process file name: {ex.Message}");
            }
            return realWindow;
        }

        #endregion

        #region Private Methods

        private SystemWindow GetCaptureWindowByTargetMode(Devices sourceDevice, Point point)
        {
            var targetMode = (sourceDevice & Devices.TouchPad) != 0
                ? AppConfig.TouchPadWindowTargetMode
                : AppConfig.TouchScreenWindowTargetMode;

            if (targetMode == WindowTargetMode.ActiveWindow)
            {
                var foreground = SystemWindow.ForegroundWindow;

                // 触控板设备：尝试鼠标位置检测或优先级窗口匹配
                if ((sourceDevice & Devices.TouchPad) != 0)
                {
                    // 功能1：鼠标移动检测（独立功能）
                    var mouseDetectedWindow = TryGetMousePositionWindow(foreground);
                    if (mouseDetectedWindow != null)
                    {
                        return mouseDetectedWindow;
                    }

                    // 功能2：优先级窗口匹配（独立功能）
                    var priorityWindow = TryGetPriorityMatchedWindow(foreground);
                    if (priorityWindow != null)
                    {
                        return priorityWindow;
                    }

                    return foreground ?? GetWindowFromPoint(System.Windows.Forms.Cursor.Position);
                }

                // 触摸屏设备：固定使用前台窗口，如果最小化则使用触摸点
                if (foreground != null && foreground.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                {
                    return GetWindowFromPoint(point);
                }
                return foreground ?? GetWindowFromPoint(point);
            }

            return GetWindowFromPoint(point);
        }

        /// <summary>
        /// 尝试获取鼠标位置的目标窗口（鼠标移动检测功能）
        /// 如果启用且鼠标位置变化，直接返回鼠标窗口
        /// </summary>
        private SystemWindow TryGetMousePositionWindow(SystemWindow foregroundWindow)
        {
            // 1. 获取前台窗口对应的应用
            var foregroundApps = GetApplicationFromWindow(foregroundWindow, true);
            var foregroundApp = foregroundApps.FirstOrDefault();
            var globalApp = GetGlobalApplication();

            // 2. 确定检测模式（应用级别覆盖全局级别）
            var mode = MouseWindowDetectionMode.Disabled;
            if (foregroundApp != null && foregroundApp.MouseWindowDetection != MouseWindowDetectionMode.Default)
            {
                mode = foregroundApp.MouseWindowDetection;
            }
            else
            {
                mode = globalApp.MouseWindowDetection;
            }

            // 3. 如果禁用，直接返回 null
            if (mode == MouseWindowDetectionMode.Disabled)
            {
                return null;
            }

            // 4. 获取鼠标位置
            var currentMousePosition = System.Windows.Forms.Cursor.Position;

            // 5. 检查鼠标位置是否变化
            if (currentMousePosition != _lastTouchPadGestureMousePosition)
            {
                _lastTouchPadGestureMousePosition = currentMousePosition;

                // 获取鼠标所在窗口并直接返回
                var mouseWindow = GetWindowFromPoint(currentMousePosition);
                if (mouseWindow != null && mouseWindow.IsValid())
                {
                    Logging.LogDebug($"[ApplicationManager] MouseWindowDetection: Mouse moved, using mouse window 0x{mouseWindow.HWnd:X} '{mouseWindow.Title}'");
                    return mouseWindow;
                }
            }

            // 更新记录位置
            _lastTouchPadGestureMousePosition = currentMousePosition;
            return null;
        }

        /// <summary>
        /// 尝试获取匹配优先级窗口的目标窗口（优先级窗口功能）
        /// </summary>
        private SystemWindow TryGetPriorityMatchedWindow(SystemWindow foregroundWindow)
        {
            // 1. 获取前台窗口对应的应用（取第一个匹配的）
            var foregroundApps = GetApplicationFromWindow(foregroundWindow, true);
            var foregroundApp = foregroundApps.FirstOrDefault();

            var globalApp = GetGlobalApplication();

            // 2. 检查是否配置了优先级窗口
            bool appHasPriorityWindows = foregroundApp?.PriorityWindows?.Count > 0;
            bool globalHasPriorityWindows = globalApp.PriorityWindows?.Count > 0;

            // 3. 如果都没配置，直接返回 null
            if (!appHasPriorityWindows && !globalHasPriorityWindows)
            {
                return null;
            }

            // 4. 获取鼠标所在窗口
            var mouseWindow = GetWindowFromPoint(System.Windows.Forms.Cursor.Position);
            if (mouseWindow == null)
            {
                return null;
            }

            // 5. 创建窗口信息缓存对象（按需获取属性）
            var windowInfo = new WindowInfoCache(mouseWindow);

            // 6. 先匹配应用级别的优先级窗口
            if (appHasPriorityWindows)
            {
                foreach (var priorityConditions in foregroundApp.PriorityWindows)
                {
                    if (MatchAllConditions(windowInfo, priorityConditions))
                    {
                        LogPriorityWindowMatch(mouseWindow, windowInfo, foregroundWindow, priorityConditions, foregroundApp.Name);
                        return mouseWindow;
                    }
                }
            }

            // 7. 再匹配全局优先级窗口
            if (globalHasPriorityWindows)
            {
                foreach (var priorityConditions in globalApp.PriorityWindows)
                {
                    if (MatchAllConditions(windowInfo, priorityConditions))
                    {
                        LogPriorityWindowMatch(mouseWindow, windowInfo, foregroundWindow, priorityConditions, "Global");
                        return mouseWindow;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 匹配一组条件（AND 逻辑：全部条件都要满足）
        /// </summary>
        private bool MatchAllConditions(WindowInfoCache windowInfo, List<MatchCondition> conditions)
        {
            if (conditions == null || conditions.Count == 0)
            {
                return false;  // 空条件不匹配
            }

            foreach (var condition in conditions)
            {
                // 跳过未配置的条件（Value 为空）
                if (string.IsNullOrEmpty(condition.Value))
                {
                    continue;
                }

                if (!MatchSingleCondition(windowInfo, condition))
                {
                    return false;  // 任一条件不匹配，整体失败
                }
            }

            return true;
        }

        /// <summary>
        /// 匹配单个条件（按需获取窗口属性）
        /// </summary>
        private bool MatchSingleCondition(WindowInfoCache windowInfo, MatchCondition condition)
        {
            return condition.Type switch
            {
                MatchConditionType.ClassName =>
                    MatchValue(windowInfo.GetClassName(), condition.Value, condition.IsRegex),
                MatchConditionType.Title =>
                    MatchValue(windowInfo.GetTitle(), condition.Value, condition.IsRegex),
                MatchConditionType.ProcessName =>
                    MatchProcessName(windowInfo.GetProcessName(), condition.Value),
                MatchConditionType.ProcessPath =>
                    MatchProcessPath(windowInfo.GetProcessPath(), condition.Value),
                MatchConditionType.AUMID =>
                    MatchValue(windowInfo.GetAUMID(), condition.Value, false),
                _ => false
            };
        }

        /// <summary>
        /// 匹配字符串值
        /// </summary>
        private bool MatchValue(string actualValue, string expectedValue, bool isRegex)
        {
            if (string.IsNullOrEmpty(actualValue))
                return false;

            if (isRegex)
            {
                try
                {
                    return Regex.IsMatch(actualValue, expectedValue, RegexOptions.IgnoreCase);
                }
                catch
                {
                    return false;
                }
            }

            return string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 匹配进程名（自动处理 .exe 后缀）
        /// </summary>
        private bool MatchProcessName(string actualProcessName, string expectedProcessName)
        {
            if (string.IsNullOrEmpty(actualProcessName))
                return false;

            var expected = expectedProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(expectedProcessName)
                : expectedProcessName;

            return string.Equals(actualProcessName, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 匹配进程路径
        /// </summary>
        private bool MatchProcessPath(string actualProcessPath, string expectedProcessPath)
        {
            if (string.IsNullOrEmpty(actualProcessPath))
                return false;

            return string.Equals(actualProcessPath, expectedProcessPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 记录优先级窗口匹配成功的日志
        /// </summary>
        private static void LogPriorityWindowMatch(SystemWindow priorityWindow, WindowInfoCache priorityWindowInfo, SystemWindow foregroundWindow, List<MatchCondition> conditions, string appName)
        {
            try
            {
                var conditionsStr = string.Join(" && ", conditions
                    .Where(c => !string.IsNullOrEmpty(c.Value))
                    .Select(c => $"{c.Type}={c.Value}"));

                // 只打印匹配条件中涉及的属性（这些属性在匹配时已被缓存，无额外开销）
                var priorityInfoParts = new List<string>();
                var usedTypes = conditions
                    .Where(c => !string.IsNullOrEmpty(c.Value))
                    .Select(c => c.Type)
                    .Distinct();

                foreach (var type in usedTypes)
                {
                    switch (type)
                    {
                        case MatchConditionType.ClassName:
                            priorityInfoParts.Add($"class={priorityWindowInfo.GetClassName()}");
                            break;
                        case MatchConditionType.Title:
                            priorityInfoParts.Add($"title={priorityWindowInfo.GetTitle()}");
                            break;
                        case MatchConditionType.ProcessName:
                            priorityInfoParts.Add($"processName={priorityWindowInfo.GetProcessName()}");
                            break;
                        case MatchConditionType.ProcessPath:
                            priorityInfoParts.Add($"processPath={priorityWindowInfo.GetProcessPath()}");
                            break;
                        case MatchConditionType.AUMID:
                            priorityInfoParts.Add($"aumid={priorityWindowInfo.GetAUMID() ?? "null"}");
                            break;
                    }
                }

                var priorityInfo = priorityInfoParts.Count > 0 ? $" ({string.Join(", ", priorityInfoParts)})" : "";

                Logging.LogDebug($"[ApplicationManager] PriorityWindow matched ({appName}): [{conditionsStr}] -> 0x{priorityWindow?.HWnd:X} '{priorityWindow?.Title}'{priorityInfo}, ForegroundWindow=0x{foregroundWindow?.HWnd:X} '{foregroundWindow?.Title}'");
            }
            catch
            {
                // 日志记录失败不影响功能
            }
        }

        private IApplication[] FindMatchApplications(IEnumerable<IApplication> applications, SystemWindow window, string className, string title, string fileName)
        {
            var result = new List<IApplication>();
            foreach (var app in applications)
            {
                try
                {
                    if (WindowMatcher.IsMatch(window, app))
                    {
                        result.Add(app);
                    }
                }
                catch
                {
                    // ignored
                }
            }
            return result.ToArray();
        }

#pragma warning disable CS0618
        private bool LoadLegacy()
        {
            var legacyApps = FileManager.LoadObject<List<LegacyApplicationBase>>(Path.Combine(AppConfig.ApplicationDataPath, "Actions.act"), true, true);
            if (legacyApps == null) return false;
            _applications = new List<IApplication>();
            foreach (var app in legacyApps)
            {
                var legacyUserApp = app as UserApplication;
                if (legacyUserApp != null)
                {
                    var newApp = new UserApp()
                    {
                        Actions = ConvertLegacyActions(legacyUserApp.Actions),
                        BlockTouchInputThreshold = legacyUserApp.BlockTouchInputThreshold,
                        LimitNumberOfFingers = legacyUserApp.LimitNumberOfFingers,
                        Group = legacyUserApp.Group,
                        MatchConditions = ConvertLegacyMatchConditions(legacyUserApp.MatchUsing, legacyUserApp.MatchString, legacyUserApp.IsRegEx),
                        Name = legacyUserApp.Name
                    };
                    _applications.Add(newApp);
                    continue;
                }

                var legacyIgnoredApp = app as IgnoredApplication;
                if (legacyIgnoredApp != null)
                {
                    var temp = legacyIgnoredApp.Name.Split(new[] { '$' }, StringSplitOptions.RemoveEmptyEntries);
                    var newName = temp.Length > 1 ? temp[1] : legacyIgnoredApp.Name;
                    var newApp = new IgnoredApp(newName,
                        ConvertLegacyMatchConditions(legacyIgnoredApp.MatchUsing, legacyIgnoredApp.MatchString, legacyIgnoredApp.IsRegEx),
                        legacyIgnoredApp.IsEnabled);
                    _applications.Add(newApp);
                    continue;
                }

                var legacyGlobalApp = app as GlobalApplication;
                if (legacyGlobalApp != null)
                {
                    IApplication newApp = new GlobalApp()
                    {
                        Actions = ConvertLegacyActions(legacyGlobalApp.Actions)
                    };
                    _applications.Add(newApp);
                    continue;
                }
            }

            return true;
        }

        private static List<MatchCondition> ConvertLegacyMatchConditions(MatchUsing matchUsing, string matchString, bool isRegEx)
        {
            if (string.IsNullOrEmpty(matchString))
                return new List<MatchCondition>();

            var conditionType = matchUsing switch
            {
                MatchUsing.WindowClass => MatchConditionType.ClassName,
                MatchUsing.WindowTitle => MatchConditionType.Title,
                MatchUsing.ExecutableFilename => MatchConditionType.ProcessName,
                _ => MatchConditionType.ProcessName
            };

            return new List<MatchCondition>
            {
                new MatchCondition
                {
                    Type = conditionType,
                    Value = matchString,
                    IsRegex = isRegEx && conditionType == MatchConditionType.Title
                }
            };
        }

        private List<IAction> ConvertLegacyActions(List<GestureSign.Applications.Action> legacyActions)
        {
            if (legacyActions == null) return null;
            List<IAction> newActions = new List<IAction>();
            foreach (var grouping in legacyActions.GroupBy(a => a.GestureName))
            {
                IAction newAction = new Action()
                {
                    ActivateWindow = grouping.First().ActivateWindow,
                    Condition = grouping.First().Condition,
                    GestureName = grouping.Key,
                    Name = grouping.First().Name,
                };
                foreach (var legacyAction in grouping)
                {
                    newAction.AddCommand(new Command
                    {
                        CommandSettings = legacyAction.ActionSettings,
                        IsEnabled = legacyAction.IsEnabled,
                        PluginClass = legacyAction.PluginClass,
                        PluginFilename = legacyAction.PluginFilename
                    });
                }
                newActions.Add(newAction);
            }
            return newActions;
        }
#pragma warning restore CS0618  

        private bool IsFullScreenWindow(Point targetPoint)
        {
            SystemWindow deskWindow = SystemWindow.DesktopWindow;

            SystemWindow sw = SystemWindow.FromPoint(targetPoint.X, targetPoint.Y);
            if (sw == null) return false;

            // get the window with the largest area
            RECT rect = sw.Rectangle;
            int area = rect.Height * rect.Width;
            while (sw.ParentSymmetric != null)
            {
                sw = sw.ParentSymmetric;
                RECT parentRect = sw.Rectangle;
                int parentArea = parentRect.Height * parentRect.Width;
                if (parentArea > area)
                {
                    area = parentArea;
                    rect = parentRect;
                }
            }

            if (sw.HWnd == IntPtr.Zero || sw == deskWindow || sw == SystemWindow.ShellWindow)
                return false;

            var desktopRect = deskWindow.Rectangle;

            if (rect.Left == desktopRect.Left && rect.Top == desktopRect.Top && rect.Right == desktopRect.Right && rect.Bottom == desktopRect.Bottom)
            {
                switch (sw.ClassName)
                {
                    case "WorkerW":
                    case "Progman":
                    case "CanvasWindow":
                    case "ImmersiveLauncher":
                        return false;
                    default:
                        return true;
                }
            }

            return false;
        }

        private void TrimActions(IEnumerable<IApplication> applications)
        {
            foreach (var app in applications)
            {
                if (app.Actions == null) continue;
                var emptyActions = app.Actions.Where(a => a.Commands == null || !a.Commands.Any()).ToList();
                emptyActions.ForEach(a => app.RemoveAction(a));
            }
        }

        #endregion

        #region P/Invoke
        [DllImport("user32.dll", EntryPoint = "FindWindow")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        #endregion
    }
}
