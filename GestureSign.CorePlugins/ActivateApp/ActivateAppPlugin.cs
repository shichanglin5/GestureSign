using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Common.Localization;
using ManagedWinapi.Windows;

#pragma warning disable CA1416 // Platform-specific API

namespace GestureSign.CorePlugins.ActivateApp
{
    public class ActivateAppPlugin : IPlugin
    {
        #region Private Variables

        private ActivateAppUI _gui;
        private ActivateAppSettings _settings;
        private IHostControl _hostControl;

        // Dictionary to track last activated window per application
        private static Dictionary<string, IntPtr> _lastActivatedWindows = new();

        // Window list cache: Key = cache key from settings, Value = (WindowHandles, LastUpdateTime)
        private readonly Dictionary<string, (List<IntPtr> Handles, DateTime LastUpdate)> _windowListCache = new();

        // Settings cache: Key = serializedData (JSON string), Value = deserialized settings
        // 注意：这里缓存的是不可变数据，UI 编辑时会生成新的 JSON 字符串
        private static readonly Dictionary<string, ActivateAppSettings> _settingsCache = new();
        private static readonly HashSet<string> WindowClassBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "IME",
            "MSCTFIME UI",
            "GDI+ Hook Window Class"
        };
        private static readonly HashSet<string> DeprioritizedShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            // Some apps expose a titled shell/root window that is foreground-capable but not user-interactive.
            "TApplication"
        };

        #endregion

        #region PInvoke Declarations

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetLastActivePopup(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private const uint GW_OWNER = 4;
        private const uint GW_HWNDPREV = 3;
        private const uint GA_ROOTOWNER = 3;
        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const int DWMWA_CLOAKED = 14;

        #endregion

        #region IPlugin Properties

        public string Name => LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.Name");

        public string Description
        {
            get
            {
                if (_settings == null || _settings.WindowRule == null)
                    return LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.Description");

                // Build description with matching info
                string matchInfo = GetMatchInfoString();
                string displayName = _settings.WindowRule.Name ?? "Unknown";

                if (string.IsNullOrEmpty(matchInfo))
                {
                    return string.Format(
                        LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.SpecificDescription"),
                        displayName);
                }

                return $"{displayName} [{matchInfo}]";
            }
        }

        /// <summary>
        /// Get matching info string for description/logging
        /// </summary>
        private string GetMatchInfoString()
        {
            if (_settings?.WindowRule == null)
                return string.Empty;

            var rule = _settings.WindowRule;
            if (rule.Conditions != null && rule.Conditions.Count > 0)
            {
                return string.Join(", ", rule.Conditions.Select(c => $"{c.Type}={c.Value}"));
            }
            else if (!string.IsNullOrEmpty(rule.ApplicationPath))
            {
                return $"AppPath={rule.ApplicationPath}";
            }

            return string.Empty;
        }

        public object GUI => _gui ?? (_gui = CreateGUI());

        public bool ActivateWindowDefault => false;

        public string Category => "Windows";

        public bool IsAction => true;

        public object Icon => IconSource.Window;

        public IHostControl HostControl
        {
            get => _hostControl;
            set => _hostControl = value;
        }

        #endregion

        #region IPlugin Methods

        public void Initialize()
        {
            // Settings cache naturally invalidates when configuration changes
            // Window list cache uses matching configuration as key, so it naturally invalidates too
        }

        public bool Gestured(PointInfo actionPoint)
        {
            try
            {
                if (_settings == null || !_settings.HasValidConditions)
                {
                    Logging.LogDebug("[ActivateApp] No valid matching conditions configured");
                    return false;
                }

                // Get matching windows
                var appWindows = GetMatchingWindows(_settings);

                // Log matching result using GetMatchInfoString
                var matchInfo = GetMatchInfoString();
                Logging.LogDebug($"[ActivateApp] Activating: {_settings.WindowRule?.Name}, matched by [{matchInfo}]: {appWindows.Count} windows found");

                if (appWindows.Count == 0)
                {
                    // Application not running - try to launch
                    return TryLaunchApplication(_settings);
                }

                // 壳窗口兜底唤醒：当进程在运行但只剩壳窗口（如 Delphi TApplication）时，
                // ShowWindow/SetForeground 无法恢复业务主窗口。
                // 通过再次启动 exe 触发单实例应用的唤醒逻辑来恢复窗口。
                if (AllWindowsAreShellOnly(appWindows))
                {
                    Logging.LogDebug($"[ActivateApp] All {appWindows.Count} windows are shell-only, launching app to wake up");
                    if (TryLaunchApplication(_settings))
                        return true;

                    Logging.LogWarning("[ActivateApp] Shell-only wake-up launch failed, fallback to direct window activation");
                }

                if (appWindows.Count == 1)
                {
                    // Single window behavior: toggle activation/minimize
                    return HandleSingleWindow(appWindows[0]);
                }
                else
                {
                    // Multi-window behavior: cycle through windows
                    return HandleMultiWindow(appWindows);
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateApp] Error: {ex.Message}");
                return false;
            }
        }

        public bool Deserialize(string serializedData)
        {
            // 从缓存获取，避免重复反序列化
            if (_settingsCache.TryGetValue(serializedData, out var cached))
            {
                _settings = cached;
                return true;
            }

            // 反序列化并缓存
            bool success = PluginHelper.DeserializeSettings(serializedData, out _settings);
            if (success && _settings != null)
            {
                _settingsCache[serializedData] = _settings;
            }
            return success;
        }

        public string Serialize()
        {
            if (_gui != null)
                _settings = _gui.Settings;

            if (_settings == null)
                _settings = new ActivateAppSettings();

            return PluginHelper.SerializeSettings(_settings);
        }

        #endregion

        #region Core Logic Methods

        /// <summary>
        /// Get windows matching the configured conditions
        /// </summary>
        private List<IntPtr> GetMatchingWindows(ActivateAppSettings settings)
        {
            string cacheKey = settings.GenerateCacheKey();

            // Try cache first (if enabled)
            if (settings.CacheExpirationSeconds > 0 && _windowListCache.TryGetValue(cacheKey, out var cachedData))
            {
                var elapsed = (DateTime.Now - cachedData.LastUpdate).TotalSeconds;
                if (elapsed <= settings.CacheExpirationSeconds)
                {
                    // Validate cached windows still exist
                    var validWindows = cachedData.Handles
                        .Where(hWnd => IsWindow(hWnd) && IsSwitchableWindow(hWnd))
                        .ToList();

                    if (validWindows.Count > 0)
                    {
                        // 只有窗口数量变化时才更新缓存，保持原始时间戳
                        if (validWindows.Count != cachedData.Handles.Count)
                        {
                            _windowListCache[cacheKey] = (validWindows, cachedData.LastUpdate);
                        }
                        return validWindows;
                    }
                }

                // Cache expired or invalid
                _windowListCache.Remove(cacheKey);
            }

            // Perform full scan
            var windows = ScanMatchingWindows(settings, includeHidden: false);

            // If no visible windows found, try hidden windows (tray apps)
            if (windows.Count == 0)
            {
                windows = ScanMatchingWindows(settings, includeHidden: true);
                if (windows.Count > 0)
                {
                    Logging.LogDebug($"[ActivateApp] Found {windows.Count} hidden windows (tray app)");
                }
            }

            // Update cache
            if (settings.CacheExpirationSeconds > 0 && windows.Count > 0)
            {
                _windowListCache[cacheKey] = (new List<IntPtr>(windows), DateTime.Now);
            }

            return windows;
        }

        /// <summary>
        /// Scan all windows and find matching ones
        /// Uses WindowRule.Conditions if available, otherwise falls back to WindowRule.ApplicationPath matching
        /// </summary>
        private List<IntPtr> ScanMatchingWindows(ActivateAppSettings settings, bool includeHidden)
        {
            var windows = new List<IntPtr>();
            var rule = settings.WindowRule;
            if (rule == null)
                return windows;

            bool hasConditions = rule.Conditions != null && rule.Conditions.Count > 0;
            bool hasApplicationPath = !string.IsNullOrEmpty(rule.ApplicationPath);

            EnumWindows((hWnd, _) =>
            {
                // Check if window is suitable for activation
                if (!includeHidden && !IsSwitchableWindow(hWnd))
                    return true;

                if (includeHidden && !IsActivatableHiddenWindow(hWnd))
                    return true;

                try
                {
                    var window = new SystemWindow(hWnd);

                    if (hasConditions)
                    {
                        // Use WindowMatcher's unified matching logic for explicit conditions
                        if (WindowMatcher.MatchAllConditions(window, rule.Conditions))
                        {
                            windows.Add(hWnd);
                        }
                    }
                    else if (hasApplicationPath)
                    {
                        // Fallback: match ProcessPath against ApplicationPath when no conditions specified
                        var processPath = WindowMatcher.GetProcessPath(hWnd);
                        if (!string.IsNullOrEmpty(processPath) &&
                            string.Equals(processPath, rule.ApplicationPath, StringComparison.OrdinalIgnoreCase))
                        {
                            windows.Add(hWnd);
                        }
                    }
                }
                catch
                {
                    // Process may have exited or access denied
                }

                return true;
            }, IntPtr.Zero);

            return windows;
        }

        private bool IsActivatableHiddenWindow(IntPtr hWnd)
        {
            if (!IsWindow(hWnd))
                return false;

            // Hidden scan is only for tray-like hidden windows.
            if (IsWindowVisible(hWnd))
                return false;

            // Keep only top-level windows.
            if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                return false;

            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // Skip tool windows unless they have WS_EX_APPWINDOW
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
                return false;

            // Skip non-activatable windows.
            if ((exStyle & WS_EX_NOACTIVATE) != 0)
                return false;

            var className = new StringBuilder(256);
            string classNameText = string.Empty;
            if (GetClassName(hWnd, className, className.Capacity) > 0)
            {
                classNameText = className.ToString();
                if (WindowClassBlacklist.Contains(classNameText))
                    return false;
            }

            // 无标题隐藏窗口几乎不可能是用户期望激活的业务窗口（如 Chrome_WidgetWin_0 辅助窗口）。
            // 壳类窗口（如 TApplication）放行，由 AllWindowsAreShellOnly 判断是否走启动兜底。
            if (GetWindowTextLength(hWnd) == 0 && !DeprioritizedShellWindowClasses.Contains(classNameText))
                return false;

            return true;
        }

        private bool HandleSingleWindow(IntPtr hWnd)
        {
            var foregroundWindow = GetForegroundWindow();
            IntPtr targetWindow = ResolvePreferredActivationWindow(hWnd);
            var window = new SystemWindow(targetWindow);
            bool isForeground = IsSameWindowGroup(targetWindow, foregroundWindow);
            bool isVisible = IsWindowVisible(targetWindow);
            var windowState = window.WindowState;

            Logging.LogDebug($"[ActivateApp] HandleSingleWindow: target={DescribeWindow(targetWindow)}, foreground={DescribeWindow(foregroundWindow)}, isForeground={isForeground}, isVisible={isVisible}, windowState={windowState}");

            if (!isVisible || windowState == FormWindowState.Minimized)
            {
                bool result = SystemWindow.TryActivateWindow(targetWindow, showHidden: true, restoreMinimized: true);
                Logging.LogDebug($"[ActivateApp] TryActivateWindow(showHidden) → {result}, actual foreground={DescribeWindow(GetForegroundWindow())}");
                return true;
            }

            // Window is not minimized, check if it's the foreground window
            if (isForeground)
            {
                // Window is already foreground and not minimized
                if (_settings.MinimizeIfActivated)
                {
                    var windowToMinimize = foregroundWindow != IntPtr.Zero &&
                                           IsSameWindowGroup(targetWindow, foregroundWindow)
                        ? foregroundWindow
                        : targetWindow;
                    new SystemWindow(windowToMinimize).WindowState = FormWindowState.Minimized;
                }
                return true;
            }
            else
            {
                // Window is background - activate it
                bool result = SystemWindow.TryActivateWindow(targetWindow);
                Logging.LogDebug($"[ActivateApp] TryActivateWindow → {result}, actual foreground={DescribeWindow(GetForegroundWindow())}");
                return true;
            }
        }

        private bool HandleMultiWindow(List<IntPtr> windows)
        {
            // Sort windows by Z-order (most recently activated first)
            windows = windows.OrderByDescending(w => GetWindowZOrder(w)).ToList();

            string appKey = _settings.GenerateCacheKey();
            IntPtr foreground = GetForegroundWindow();

            // Check if the current foreground window belongs to this application and is not minimized
            // A minimized foreground window (e.g. just toggled off) should be treated as "coming from another app"
            // to allow restoring via _lastActivatedWindows
            bool foregroundIsThisApp = foreground != IntPtr.Zero &&
                                       !IsIconic(foreground) &&
                                       windows.Any(w => IsSameWindowGroup(w, foreground));

            IntPtr targetWindow;

            if (foregroundIsThisApp)
            {
                // Already in this app - try to cycle to the next non-minimized window
                int currentIndex = windows.FindIndex(w => IsSameWindowGroup(w, foreground));
                if (currentIndex < 0)
                    currentIndex = windows.IndexOf(foreground);
                targetWindow = IntPtr.Zero;
                for (int i = 1; i < windows.Count; i++)
                {
                    int candidateIndex = (currentIndex + i) % windows.Count;
                    if (!IsIconic(windows[candidateIndex]))
                    {
                        targetWindow = windows[candidateIndex];
                        break;
                    }
                }

                if (targetWindow == IntPtr.Zero)
                {
                    // No non-minimized candidate - minimize current window (like HandleSingleWindow toggle)
                    if (_settings.MinimizeIfActivated)
                    {
                        // Record current window so next trigger (all minimized) restores it deterministically
                        _lastActivatedWindows[appKey] = foreground;
                        new SystemWindow(foreground).WindowState = FormWindowState.Minimized;
                    }
                    return true;
                }
            }
            else
            {
                // Coming from another app - prefer non-minimized windows
                // Avoid restoring minimized windows when visible ones exist
                var nonMinimizedWindows = windows.Where(w => !IsIconic(w)).ToList();

                _lastActivatedWindows.TryGetValue(appKey, out IntPtr lastWindow);

                if (lastWindow != IntPtr.Zero)
                {
                    // Last window exists and is not minimized - use it
                    if (nonMinimizedWindows.Contains(lastWindow))
                    {
                        targetWindow = lastWindow;
                        Logging.LogDebug($"[ActivateApp] From other app, restoring last window: {DescribeWindow(targetWindow)}");
                    }
                    // Last window is unavailable (minimized or closed) but there are non-minimized windows - use topmost non-minimized
                    else if (nonMinimizedWindows.Count > 0)
                    {
                        targetWindow = nonMinimizedWindows[0];
                        Logging.LogDebug($"[ActivateApp] From other app, last window unavailable, using topmost visible: {DescribeWindow(targetWindow)}");
                    }
                    // All windows minimized - fall back to last window
                    else if (windows.Contains(lastWindow))
                    {
                        targetWindow = lastWindow;
                        Logging.LogDebug($"[ActivateApp] From other app, all minimized, restoring last window: {DescribeWindow(targetWindow)}");
                    }
                    else
                    {
                        targetWindow = windows[0];
                        Logging.LogDebug($"[ActivateApp] From other app, no valid last window, using topmost: {DescribeWindow(targetWindow)}");
                    }
                }
                else
                {
                    // No last window - use topmost non-minimized, or topmost overall
                    targetWindow = nonMinimizedWindows.Count > 0 ? nonMinimizedWindows[0] : windows[0];
                    Logging.LogDebug($"[ActivateApp] From other app, no last window, using topmost: {DescribeWindow(targetWindow)}");
                }
            }

            IntPtr resolvedTargetWindow = ResolvePreferredActivationWindow(targetWindow);

            // Activate the target window (show hidden + restore minimized)
            bool result = SystemWindow.TryActivateWindow(resolvedTargetWindow, showHidden: true, restoreMinimized: true);
            Logging.LogDebug($"[ActivateApp] TryActivateWindow(multi) → {result}, actual foreground={DescribeWindow(GetForegroundWindow())}");

            // Update last activated window
            _lastActivatedWindows[appKey] = targetWindow;
            return true;
        }

        /// <summary>
        /// Try to launch application using WindowRule.ApplicationPath and ApplicationArguments
        /// For PWA/UWP apps, use AUMID to launch via shell:AppsFolder
        /// </summary>
        private bool TryLaunchApplication(ActivateAppSettings settings)
        {
            // 优先尝试通过 AUMID 启动（PWA/UWP 应用）
            if (!string.IsNullOrEmpty(settings.AUMID))
            {
                return TryLaunchByAUMID(settings.AUMID);
            }

            // 回退到传统的 exe 路径启动
            var applicationPath = settings.WindowRule?.ApplicationPath;
            if (string.IsNullOrEmpty(applicationPath))
            {
                Logging.LogWarning($"[ActivateApp] No application path or AUMID, cannot launch");
                return false;
            }

            try
            {
                if (!File.Exists(applicationPath))
                {
                    Logging.LogWarning($"[ActivateApp] Application not found: {applicationPath}");
                    return false;
                }

                Logging.LogDebug($"[ActivateApp] Launching application: {applicationPath} {settings.ApplicationArguments}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = applicationPath,
                    Arguments = settings.ApplicationArguments ?? string.Empty,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateApp] Failed to launch application: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Launch PWA/UWP application by AUMID using shell:AppsFolder protocol
        /// </summary>
        private bool TryLaunchByAUMID(string aumid)
        {
            try
            {
                Logging.LogDebug($"[ActivateApp] Launching app by AUMID: {aumid}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{aumid}",
                    UseShellExecute = false
                };

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogError($"[ActivateApp] Failed to launch app by AUMID: {ex.Message}");
                return false;
            }
        }

        private bool IsSwitchableWindow(IntPtr hWnd)
        {
            // Must be visible (even if minimized)
            if (!IsWindowVisible(hWnd))
                return false;

            // Must have a title
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return false;

            // Skip windows with an owner (child windows like SubWebView)
            IntPtr ownerWindow = GetWindow(hWnd, GW_OWNER);
            if (ownerWindow != IntPtr.Zero)
                return false;

            // Check extended window styles
            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // Skip tool windows unless they have WS_EX_APPWINDOW
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
                return false;

            // Skip non-activatable windows.
            if ((exStyle & WS_EX_NOACTIVATE) != 0)
                return false;

            // Skip DWM cloaked windows (can report visible but not user-interactable).
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            // Skip known internal/system window classes (e.g. GDI+ Hook Window)
            string className = GetWindowClassName(hWnd);
            if (!string.IsNullOrEmpty(className) && WindowClassBlacklist.Contains(className))
                return false;

            return true;
        }

        private IntPtr ResolvePreferredActivationWindow(IntPtr hWnd)
        {
            if (!IsWindow(hWnd))
                return hWnd;

            // Gate 1: If rule has an explicit ClassName condition and the input window matches it,
            // this is a precisely matched window — skip resolution to avoid dialog/popup override.
            var conditions = _settings?.WindowRule?.Conditions;
            if (conditions != null)
            {
                var classCondition = conditions.FirstOrDefault(c => c.Type == MatchConditionType.ClassName);
                if (classCondition != null && !string.IsNullOrEmpty(classCondition.Value))
                {
                    string inputClassName = GetWindowClassName(hWnd);
                    if (string.Equals(inputClassName, classCondition.Value, StringComparison.OrdinalIgnoreCase))
                        return hWnd;
                }
            }

            // Gate 2: If the input window has a title and is not a known shell-only class,
            // it is likely a real business window — skip resolution.
            string className = GetWindowClassName(hWnd);
            bool isShellWindow = !string.IsNullOrEmpty(className)
                && DeprioritizedShellWindowClasses.Contains(className);
            if (GetWindowTextLength(hWnd) > 0 && !isShellWindow)
                return hWnd;

            // The input window is a shell/framework window — run full resolution.
            IntPtr rootOwner = GetAncestor(hWnd, GA_ROOTOWNER);
            if (rootOwner == IntPtr.Zero || !IsWindow(rootOwner))
                rootOwner = hWnd;

            GetWindowThreadProcessId(rootOwner, out int rootPid);
            IntPtr lastActivePopup = GetLastActivePopup(rootOwner);
            int currentScore = EvaluateActivationCandidateScore(hWnd, rootOwner, rootPid, lastActivePopup, requireSameRootOwner: true);
            IntPtr bestWindow = hWnd;
            int bestScore = currentScore;

            int popupScore = EvaluateActivationCandidateScore(lastActivePopup, rootOwner, rootPid, lastActivePopup, requireSameRootOwner: true);
            if (popupScore > bestScore)
            {
                bestWindow = lastActivePopup;
                bestScore = popupScore;
            }

            EnumWindows((candidate, _) =>
            {
                int score = EvaluateActivationCandidateScore(candidate, rootOwner, rootPid, lastActivePopup, requireSameRootOwner: true);
                if (score > bestScore)
                {
                    bestWindow = candidate;
                    bestScore = score;
                }

                return true;
            }, IntPtr.Zero);

            // Root-owner group may not expose a suitable popup (some tray apps do this).
            // In that case, allow same-process fallback but keep strong score penalties for shell-only windows.
            if (bestWindow == hWnd || bestScore <= currentScore)
            {
                EnumWindows((candidate, _) =>
                {
                    int score = EvaluateActivationCandidateScore(candidate, rootOwner, rootPid, lastActivePopup, requireSameRootOwner: false);
                    if (score > bestScore)
                    {
                        bestWindow = candidate;
                        bestScore = score;
                    }

                    return true;
                }, IntPtr.Zero);
            }

            if (bestWindow != hWnd)
            {
                Logging.LogDebug($"[ActivateApp] ResolvePreferredActivationWindow: {DescribeWindow(hWnd)} -> best {DescribeWindow(bestWindow)} (score={bestScore}, current={currentScore})");
                return bestWindow;
            }

            return hWnd;
        }

        /// <summary>
        /// 判断是否所有窗口都是壳窗口（如 TApplication），没有真正的用户业务窗口。
        /// 这种情况下 ShowWindow/SetForeground 无法恢复应用，需要走启动路径唤醒。
        /// </summary>
        private bool AllWindowsAreShellOnly(List<IntPtr> windows)
        {
            foreach (var hWnd in windows)
            {
                string className = GetWindowClassName(hWnd);

                // 原始窗口只要不是壳类，就认为存在业务窗口。
                if (string.IsNullOrEmpty(className) || !DeprioritizedShellWindowClasses.Contains(className))
                    return false;

                // 壳窗口经 Resolve 后只要能定位到非壳类窗口，也不应走启动兜底。
                IntPtr resolved = ResolvePreferredActivationWindow(hWnd);
                string resolvedClass = GetWindowClassName(resolved);
                if (string.IsNullOrEmpty(resolvedClass) || !DeprioritizedShellWindowClasses.Contains(resolvedClass))
                    return false;
            }
            return true;
        }

        private bool IsSameWindowGroup(IntPtr hWnd1, IntPtr hWnd2)
        {
            if (hWnd1 == IntPtr.Zero || hWnd2 == IntPtr.Zero)
                return false;

            IntPtr rootOwner1 = GetAncestor(hWnd1, GA_ROOTOWNER);
            if (rootOwner1 == IntPtr.Zero)
                rootOwner1 = hWnd1;

            IntPtr rootOwner2 = GetAncestor(hWnd2, GA_ROOTOWNER);
            if (rootOwner2 == IntPtr.Zero)
                rootOwner2 = hWnd2;

            return rootOwner1 == rootOwner2;
        }

        private string DescribeWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return "0x0";

            string title = GetWindowTitle(hWnd);
            string className = GetWindowClassName(hWnd);

            return $"0x{hWnd:X} '{title}' (class={className})";
        }

        private int EvaluateActivationCandidateScore(
            IntPtr candidate,
            IntPtr rootOwner,
            int rootPid,
            IntPtr lastActivePopup,
            bool requireSameRootOwner)
        {
            if (candidate == IntPtr.Zero || !IsWindow(candidate))
                return int.MinValue;

            IntPtr candidateRootOwner = GetAncestor(candidate, GA_ROOTOWNER);
            if (candidateRootOwner == IntPtr.Zero)
                candidateRootOwner = candidate;

            if (requireSameRootOwner && candidateRootOwner != rootOwner)
                return int.MinValue;

            GetWindowThreadProcessId(candidate, out int candidatePid);
            if (rootPid != 0 && candidatePid != rootPid)
                return int.MinValue;

            uint exStyle = GetWindowLong(candidate, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
                return int.MinValue;

            if ((exStyle & WS_EX_NOACTIVATE) != 0)
                return int.MinValue;

            if (DwmGetWindowAttribute(candidate, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return int.MinValue;

            string className = GetWindowClassName(candidate);
            if (!string.IsNullOrEmpty(className) && WindowClassBlacklist.Contains(className))
                return int.MinValue;

            int score = 0;
            if (candidateRootOwner == rootOwner)
                score += 40;

            if (candidate == lastActivePopup)
                score += 35;

            if (IsWindowVisible(candidate))
                score += 25;
            else
                score += 8;

            if (!IsIconic(candidate))
                score += 25;

            if (GetWindowTextLength(candidate) > 0)
                score += 30;
            else
                score -= 12;

            if (GetWindow(candidate, GW_OWNER) != IntPtr.Zero)
                score += 10;

            if (candidate == rootOwner)
                score -= 25;

            if (!string.IsNullOrEmpty(className) && DeprioritizedShellWindowClasses.Contains(className))
                score -= 60;

            return score;
        }

        private string GetWindowClassName(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return string.Empty;

            var classBuilder = new StringBuilder(256);
            return GetClassName(hWnd, classBuilder, classBuilder.Capacity) > 0
                ? classBuilder.ToString()
                : string.Empty;
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return string.Empty;

            int titleLength = GetWindowTextLength(hWnd);
            if (titleLength <= 0)
                return string.Empty;

            var titleBuilder = new StringBuilder(titleLength + 1);
            return GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity) > 0
                ? titleBuilder.ToString()
                : string.Empty;
        }

        private int GetWindowZOrder(IntPtr hWnd)
        {
            // Higher value = higher Z-order (closer to foreground)
            int zOrder = 0;
            IntPtr current = hWnd;

            while (current != IntPtr.Zero)
            {
                current = GetWindow(current, GW_HWNDPREV);
                zOrder++;
            }

            return zOrder;
        }

        #endregion

        #region GUI Creation

        private ActivateAppUI CreateGUI()
        {
            var newGUI = new ActivateAppUI();
            newGUI.Loaded += (o, e) => { newGUI.Settings = _settings; };
            return newGUI;
        }

        #endregion
    }
}
