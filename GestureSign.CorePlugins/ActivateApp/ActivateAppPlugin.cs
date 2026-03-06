using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
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
        // Cached data is immutable for a given JSON payload.
        private static readonly Dictionary<string, ActivateAppSettings> _settingsCache = new();
        private static readonly HashSet<string> WindowClassBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "IME",
            "MSCTFIME UI",
            "GDI+ Hook Window Class",
            "TApplication"
        };
        private static readonly HashSet<string> WindowTitleBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            // TongDaXin helper root window title; activating it steals focus without showing business UI.
            "HIDENET",
            // Qt tray icon message window; not user-facing.
            "QTrayIconMessageWindow",
            // Telegram ghost window that persists after closing; visible but not activatable.
            "TelegramDesktop"
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

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private const uint GW_OWNER = 4;
        private const uint GW_HWNDPREV = 3;

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
                // Sync latest preset content when this action references a preset.
                SyncPresetIfNeeded();

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
                foreach (var w in appWindows)
                {
                    IntPtr owner = GetWindow(w, GW_OWNER);
                    Logging.LogDebug($"[ActivateApp]   {DescribeWindow(w)}, owner={DescribeWindow(owner)}");
                }

                if (appWindows.Count == 0)
                {
                    // 无可激活窗口时启动应用。
                    // 对单实例应用（如 Telegram、通达信），重复启动会恢复已有窗口而非创建新实例。
                    return TryLaunchApplication(_settings);
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
            // Retrieve from cache to avoid repeated deserialization.
            if (_settingsCache.TryGetValue(serializedData, out var cached))
            {
                _settings = cached;
                return true;
            }

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
        /// 获取符合规则的可激活窗口列表。
        /// 1. 尝试缓存命中（带 IsSwitchableWindow 验证）
        /// 2. 可见窗口扫描（IsSwitchableWindow）
        /// 3. 隐藏窗口扫描（IsActivatableHiddenWindow + FilterHiddenCandidates 后处理）
        /// requireTitle 由规则条件自动决定：包含 ClassName 条件时不要求标题。
        /// </summary>
        private List<IntPtr> GetMatchingWindows(ActivateAppSettings settings)
        {
            string cacheKey = settings.GenerateCacheKey();

            bool requireTitle = settings.WindowRule?.Conditions?.Any(c => c.Type == MatchConditionType.ClassName) != true;

            // Try cache first (if enabled)
            if (settings.CacheExpirationSeconds > 0 && _windowListCache.TryGetValue(cacheKey, out var cachedData))
            {
                var elapsed = (DateTime.Now - cachedData.LastUpdate).TotalSeconds;
                if (elapsed <= settings.CacheExpirationSeconds)
                {
                    // Validate cached windows still exist
                    var validWindows = cachedData.Handles
                        .Where(hWnd => IsWindow(hWnd) && IsSwitchableWindow(hWnd, requireTitle))
                        .ToList();

                    if (validWindows.Count > 0)
                    {
                        // Only update cache content when count changes; keep original timestamp.
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
            var windows = ScanMatchingWindows(settings, includeHidden: false, requireTitle);

            // If no visible windows found, try hidden windows (tray apps)
            if (windows.Count == 0)
            {
                windows = ScanMatchingWindows(settings, includeHidden: true, requireTitle);
                if (windows.Count > 0)
                {
                    LogHiddenCandidates("before filter", windows);
                    windows = FilterHiddenCandidates(windows);
                    LogHiddenCandidates("after filter", windows);
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
        private List<IntPtr> ScanMatchingWindows(ActivateAppSettings settings, bool includeHidden, bool requireTitle)
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
                if (!includeHidden && !IsSwitchableWindow(hWnd, requireTitle))
                    return true;

                if (includeHidden && !IsActivatableHiddenWindow(hWnd, requireTitle))
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

        /// <summary>
        /// 隐藏窗口扫描的准入判断。与 IsSwitchableWindow（可见扫描）保持一致的
        /// owner/exStyle/cloaked/class 黑名单/标题过滤规则，区别在于只接受
        /// Win32 不可见的窗口（IsWindowVisible=false）。
        /// </summary>
        private bool IsActivatableHiddenWindow(IntPtr hWnd, bool requireTitle = true)
        {
            if (!IsWindow(hWnd))
                return false;

            // Hidden scan only handles Win32-invisible windows.
            if (IsWindowVisible(hWnd))
                return false;

            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // 与 IsSwitchableWindow（可见扫描）一致的 owner 规则：
            // - WS_EX_APPWINDOW → 放行（无论 owner/tool window）
            // - WS_EX_TOOLWINDOW && !WS_EX_APPWINDOW → 过滤
            // - owner 可见且 class 不在黑名单 → 过滤（对话框/子窗口）
            // - owner 不可见 或 owner class 在黑名单（如 TApplication）→ 放行
            // - 无 owner → 放行
            if ((exStyle & WS_EX_APPWINDOW) == 0)
            {
                if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                    return false;

                IntPtr owner = GetWindow(hWnd, GW_OWNER);
                if (owner != IntPtr.Zero && IsWindowVisible(owner))
                {
                    if (DwmGetWindowAttribute(owner, DWMWA_CLOAKED, out int ownerCloaked, sizeof(int)) == 0 && ownerCloaked != 0)
                    {
                        // owner is cloaked, treat as invisible - allow this window
                    }
                    else
                    {
                        string ownerClass = GetWindowClassName(owner);
                        if (string.IsNullOrEmpty(ownerClass) || !WindowClassBlacklist.Contains(ownerClass))
                            return false;
                    }
                }
            }

            // Skip non-activatable windows.
            if ((exStyle & WS_EX_NOACTIVATE) != 0)
                return false;

            // Skip DWM cloaked windows (与 IsSwitchableWindow 保持一致).
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            // Skip known internal/system window classes (e.g. GDI+ Hook Window)
            var className = new StringBuilder(256);
            if (GetClassName(hWnd, className, className.Capacity) > 0)
            {
                if (WindowClassBlacklist.Contains(className.ToString()))
                    return false;
            }

            // Hidden windows without title are usually not user-facing business windows.
            // 当规则包含 ClassName 条件时可跳过（同 IsSwitchableWindow）。
            if (requireTitle && GetWindowTextLength(hWnd) == 0)
                return false;

            return true;
        }

        private bool HandleSingleWindow(IntPtr hWnd)
        {
            var window = new SystemWindow(hWnd);
            var foregroundWindow = GetForegroundWindow();
            bool isForeground = hWnd == foregroundWindow;
            bool isVisible = IsWindowVisible(hWnd);
            // isCloaked 仅用于诊断日志。DWM cloaked 窗口（DWMWA_CLOAKED != 0）的 IsWindowVisible
            // 仍返回 true，但窗口实际不可见不可交互。第三方进程无法通过
            // DwmSetWindowAttribute(DWMWA_CLOAK=FALSE) 解除 cloaked 状态，
            // 这类窗口会走到普通激活路径，Win32 层面激活"成功"但窗口可能仍不可见，
            // 需要应用自身（如 Telegram/Qt）通过内部状态机恢复。
            bool isCloaked = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloakedVal, sizeof(int)) == 0 && cloakedVal != 0;
            bool isIconic = IsIconic(hWnd);
            var windowState = window.WindowState;

            IntPtr owner = GetWindow(hWnd, GW_OWNER);
            Logging.LogDebug($"[ActivateApp] HandleSingleWindow: target={DescribeWindow(hWnd)}, owner={DescribeWindow(owner)}, foreground={DescribeWindow(foregroundWindow)}, isForeground={isForeground}, isVisible={isVisible}, isCloaked={isCloaked}, isIconic={isIconic}, windowState={windowState}");

            if (!isVisible)
            {
                // Hidden/tray window: restore via showHidden path.
                bool useAttach = ResolveUseAttachThreadInput();
                bool result = SystemWindow.TryActivateWindow(hWnd, showHidden: true, restoreMinimized: true, useAttachThreadInput: useAttach);
                Logging.LogDebug($"[ActivateApp] TryActivateWindow(showHidden, attach={useAttach}) -> {result}, actual foreground={DescribeWindow(GetForegroundWindow())}");
                return true;
            }

            if (isIconic)
            {
                bool useAttach = ResolveUseAttachThreadInput();
                bool result = SystemWindow.TryActivateWindow(hWnd, showHidden: false, restoreMinimized: true, useAttachThreadInput: useAttach);
                Logging.LogDebug($"[ActivateApp] TryActivateWindow(restoreMinimized, attach={useAttach}) -> {result}, actual foreground={DescribeWindow(GetForegroundWindow())}");
                return true;
            }

            if (isForeground)
            {
                // Window is already foreground, visible and not iconic.
                if (windowState == FormWindowState.Minimized)
                {
                    // Some Qt apps report WindowPlacement=Minimized while IsIconic=false.
                    // Skip extra operations to avoid triggering UI hangs.
                    Logging.LogDebug($"[ActivateApp] foreground window has inconsistent state (windowState=Minimized but isIconic=false), skipping");
                    return true;
                }

                if (_settings.MinimizeIfActivated)
                {
                    window.WindowState = FormWindowState.Minimized;
                }
                return true;
            }
            else
            {
                // Window is in background, activate it to foreground.
                bool useAttach = ResolveUseAttachThreadInput();
                bool result = SystemWindow.TryActivateWindow(hWnd, useAttachThreadInput: useAttach);
                Logging.LogDebug($"[ActivateApp] TryActivateWindow(attach={useAttach}) -> {result}, actual foreground={DescribeWindow(GetForegroundWindow())}");
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
            bool foregroundIsThisApp = windows.Contains(foreground) && !IsIconic(foreground);

            IntPtr targetWindow;

            if (foregroundIsThisApp)
            {
                // Already in this app - try to cycle to the next non-minimized window
                int currentIndex = windows.IndexOf(foreground);
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
                        _lastActivatedWindows[appKey] = foreground;
                        new SystemWindow(foreground).WindowState = FormWindowState.Minimized;
                    }
                    return true;
                }
            }
            else
            {
                // Coming from another app - prefer non-minimized windows
                var nonMinimizedWindows = windows.Where(w => !IsIconic(w)).ToList();

                _lastActivatedWindows.TryGetValue(appKey, out IntPtr lastWindow);

                if (lastWindow != IntPtr.Zero)
                {
                    if (nonMinimizedWindows.Contains(lastWindow))
                    {
                        targetWindow = lastWindow;
                        Logging.LogDebug($"[ActivateApp] From other app, restoring last window: {DescribeWindow(targetWindow)}");
                    }
                    else if (nonMinimizedWindows.Count > 0)
                    {
                        targetWindow = nonMinimizedWindows[0];
                        Logging.LogDebug($"[ActivateApp] From other app, last window unavailable, using topmost visible: {DescribeWindow(targetWindow)}");
                    }
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
                    targetWindow = nonMinimizedWindows.Count > 0 ? nonMinimizedWindows[0] : windows[0];
                    Logging.LogDebug($"[ActivateApp] From other app, no last window, using topmost: {DescribeWindow(targetWindow)}");
                }
            }

            // Activate the target window (show hidden + restore minimized)
            bool useAttach = ResolveUseAttachThreadInput();
            bool result = SystemWindow.TryActivateWindow(targetWindow, showHidden: true, restoreMinimized: true, useAttachThreadInput: useAttach);
            IntPtr targetOwner = GetWindow(targetWindow, GW_OWNER);
            Logging.LogDebug($"[ActivateApp] TryActivateWindow(multi, attach={useAttach}) -> {result}, target owner={DescribeWindow(targetOwner)}, actual foreground={DescribeWindow(GetForegroundWindow())}");

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
            // Prefer AUMID launch for PWA/UWP apps.
            if (!string.IsNullOrEmpty(settings.AUMID))
            {
                return TryLaunchByAUMID(settings.AUMID);
            }

            // Fallback to traditional executable path.
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

        /// <summary>
        /// 对 hidden scan 候选列表做后处理：
        /// 1. 黑名单标题窗口尝试用同进程 owned 业务窗口替换
        /// 2. 找出"被同进程其他候选作为 owner 引用"的窗口，降权（辅助根窗口）
        /// 3. 在剩余候选中，WS_EX_APPWINDOW 窗口排在前面
        /// </summary>
        private List<IntPtr> FilterHiddenCandidates(List<IntPtr> candidates)
        {
            if (candidates.Count == 0)
                return candidates;

            // First pass: try replacing blacklisted-title windows with owned business windows.
            var replaced = new List<IntPtr>();
            var blacklisted = new HashSet<IntPtr>();
            foreach (var candidate in candidates)
            {
                if (!IsBlacklistedWindowTitle(candidate))
                    continue;

                blacklisted.Add(candidate);
                IntPtr ownedWindow = FindOwnedWindowInSameProcess(candidate);
                if (ownedWindow != IntPtr.Zero)
                {
                    replaced.Add(ownedWindow);
                }
            }

            if (replaced.Count > 0)
            {
                // 合并：替换结果 + 非黑名单的原始候选
                var normal = candidates.Where(c => !blacklisted.Contains(c));
                var merged = replaced.Concat(normal).Distinct().ToList();
                SortByAppWindowPriority(merged);
                return merged;
            }

            // If only blacklisted-title windows remain and no replacement found, don't activate them.
            if (candidates.All(IsBlacklistedWindowTitle))
                return new List<IntPtr>();

            // 收集每个候选的 pid 和 owner
            var candidateSet = new HashSet<IntPtr>(candidates);
            var pidMap = new Dictionary<IntPtr, int>();
            var ownerMap = new Dictionary<IntPtr, IntPtr>();

            foreach (var hWnd in candidates)
            {
                GetWindowThreadProcessId(hWnd, out int pid);
                pidMap[hWnd] = pid;
                ownerMap[hWnd] = GetWindow(hWnd, GW_OWNER);
            }

            // 找出"被同进程其他候选作为 owner 引用"的窗口（辅助根窗口）
            var ownerAuxiliary = new HashSet<IntPtr>();
            foreach (var hWnd in candidates)
            {
                IntPtr owner = ownerMap[hWnd];
                if (owner != IntPtr.Zero
                    && candidateSet.Contains(owner)
                    && pidMap.TryGetValue(owner, out int ownerPid)
                    && ownerPid == pidMap[hWnd])
                {
                    ownerAuxiliary.Add(owner);
                }
            }

            // 降权：优先保留非辅助窗口
            var preferred = candidates.Where(h => !ownerAuxiliary.Contains(h)).ToList();
            if (preferred.Count == 0)
                preferred = candidates; // 兜底：全是辅助则回退原列表

            SortByAppWindowPriority(preferred);

            return preferred;
        }

        /// <summary>
        /// WS_EX_APPWINDOW 窗口排在前面。
        /// </summary>
        private static void SortByAppWindowPriority(List<IntPtr> windows)
        {
            windows.Sort((a, b) =>
            {
                bool aApp = (GetWindowLong(a, GWL_EXSTYLE) & WS_EX_APPWINDOW) != 0;
                bool bApp = (GetWindowLong(b, GWL_EXSTYLE) & WS_EX_APPWINDOW) != 0;
                return bApp.CompareTo(aApp);
            });
        }

        /// <summary>
        /// 在同进程中查找以 ownerHWnd 为 owner 的窗口（单层）。
        /// 用于单候选场景：判断候选是否是辅助根窗口，并找到被它 own 的业务窗口。
        /// 优先返回带 WS_EX_APPWINDOW 的窗口。
        /// </summary>
        private IntPtr FindOwnedWindowInSameProcess(IntPtr ownerHWnd)
        {
            GetWindowThreadProcessId(ownerHWnd, out int ownerPid);
            IntPtr bestCandidate = IntPtr.Zero;
            bool bestHasAppWindow = false;

            EnumWindows((hWnd, _) =>
            {
                if (hWnd == ownerHWnd)
                    return true;

                if (GetWindow(hWnd, GW_OWNER) != ownerHWnd)
                    return true;

                GetWindowThreadProcessId(hWnd, out int pid);
                if (pid != ownerPid)
                    return true;

                // 窗口必须有标题
                if (GetWindowTextLength(hWnd) == 0)
                    return true;

                if (IsBlacklistedWindowTitle(hWnd))
                    return true;

                if (!MatchesCurrentRule(hWnd))
                    return true;

                uint ownedExStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

                if ((ownedExStyle & WS_EX_NOACTIVATE) != 0)
                    return true;

                // 与主扫描保持一致的质量过滤
                if ((ownedExStyle & WS_EX_TOOLWINDOW) != 0 && (ownedExStyle & WS_EX_APPWINDOW) == 0)
                    return true;

                var ownedClassName = new StringBuilder(256);
                if (GetClassName(hWnd, ownedClassName, ownedClassName.Capacity) > 0)
                {
                    if (WindowClassBlacklist.Contains(ownedClassName.ToString()))
                        return true;
                }

                if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                bool hasAppWindow = (ownedExStyle & WS_EX_APPWINDOW) != 0;
                if (bestCandidate == IntPtr.Zero || (hasAppWindow && !bestHasAppWindow))
                {
                    bestCandidate = hWnd;
                    bestHasAppWindow = hasAppWindow;
                }

                return true;
            }, IntPtr.Zero);

            return bestCandidate;
        }

        private void LogHiddenCandidates(string phase, List<IntPtr> candidates)
        {
            if (candidates.Count == 0)
            {
                Logging.LogDebug($"[ActivateApp] Hidden candidates ({phase}): (none)");
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"[ActivateApp] Hidden candidates ({phase}): {candidates.Count} windows");
            foreach (var hWnd in candidates)
            {
                GetWindowThreadProcessId(hWnd, out int pid);
                IntPtr owner = GetWindow(hWnd, GW_OWNER);
                uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                string title = GetWindowTitle(hWnd);
                sb.Append($"\n  0x{hWnd:X} pid={pid} owner=0x{owner:X} exStyle=0x{exStyle:X8} title='{title}'");
            }
            Logging.LogDebug(sb.ToString());
        }

        private bool MatchesCurrentRule(IntPtr hWnd)
        {
            var rule = _settings?.WindowRule;
            if (rule == null)
                return false;

            bool hasConditions = rule.Conditions != null && rule.Conditions.Count > 0;
            bool hasApplicationPath = !string.IsNullOrEmpty(rule.ApplicationPath);

            try
            {
                if (hasConditions)
                {
                    return WindowMatcher.MatchAllConditions(new SystemWindow(hWnd), rule.Conditions);
                }

                if (hasApplicationPath)
                {
                    var processPath = WindowMatcher.GetProcessPath(hWnd);
                    return !string.IsNullOrEmpty(processPath)
                        && string.Equals(processPath, rule.ApplicationPath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Process may have exited or access denied.
            }

            return false;
        }

        private bool IsBlacklistedWindowTitle(IntPtr hWnd)
        {
            string title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return false;

            return WindowTitleBlacklist.Contains(title.Trim());
        }

        /// <summary>
        /// 可见窗口扫描的准入判断。判断逻辑接近系统任务栏/Alt+Tab 的规则：
        /// visible + 非 cloaked + 非 ToolWindow + owner 判定 + class/title 黑名单。
        /// owner 判定：有可见且非 cloaked 且 class 不在黑名单的 owner → 过滤（对话框/子窗口）。
        /// </summary>
        private bool IsSwitchableWindow(IntPtr hWnd, bool requireTitle = true)
        {
            // Must be visible (even if minimized)
            if (!IsWindowVisible(hWnd))
                return false;

            // 通常要求有标题；但当规则包含 ClassName 条件时可跳过——
            // 部分应用（如 Delphi Foxmail）的主窗口没有标题。
            if (requireTitle && GetWindowTextLength(hWnd) == 0)
                return false;

            if (IsBlacklistedWindowTitle(hWnd))
                return false;

            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // 任务栏同款规则判断窗口是否为用户可见的主窗口：
            // 1. WS_EX_APPWINDOW → 一定显示（无论 owner/tool window）
            // 2. WS_EX_TOOLWINDOW → 跳过
            // 3. 无 owner → 显示
            // 4. 有 owner → 跳过（对话框、子窗口等）
            if ((exStyle & WS_EX_APPWINDOW) == 0)
            {
                if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                    return false;

                IntPtr ownerWindow = GetWindow(hWnd, GW_OWNER);
                // 跳过有"真正可见"的 owner 的窗口（对话框、子窗口等）。
                // "真正可见" = IsWindowVisible && 非 DWM cloaked && class 不在黑名单。
                // 放行情况：
                // - owner 不可见（普通隐藏）
                // - owner cloaked（如 Telegram 旧窗口，IsWindowVisible=True 但 DWM 隐藏）
                // - owner class 在黑名单（如 Delphi TApplication，0 像素的框架消息窗口）
                if (ownerWindow != IntPtr.Zero && IsWindowVisible(ownerWindow))
                {
                    // owner 是 cloaked 窗口时视为不可见，放行 owned 窗口
                    if (DwmGetWindowAttribute(ownerWindow, DWMWA_CLOAKED, out int ownerCloaked, sizeof(int)) == 0 && ownerCloaked != 0)
                    {
                        // owner is cloaked, treat as invisible - allow this window
                    }
                    else
                    {
                        string ownerClass = GetWindowClassName(ownerWindow);
                        if (string.IsNullOrEmpty(ownerClass) || !WindowClassBlacklist.Contains(ownerClass))
                            return false;
                    }
                }
            }

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

        private string DescribeWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return "0x0";

            string title = GetWindowTitle(hWnd);
            string className = GetWindowClassName(hWnd);

            return $"0x{hWnd:X} '{title}' (class={className})";
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

        /// <summary>
        /// Resolve final activation mode with priority:
        /// preset override > action setting > global default.
        /// </summary>
        private bool ResolveUseAttachThreadInput()
        {
            if (!string.IsNullOrEmpty(_settings?.PresetId))
            {
                var preset = WindowPresetManager.Instance.GetPresetById(_settings.PresetId);
                if (preset != null && preset.ActivationMethod != ActivationMethod.UseGlobal)
                {
                    return preset.ActivationMethod == ActivationMethod.AttachThreadInput;
                }
            }

            var method = _settings?.ActivationMethod ?? ActivationMethod.UseGlobal;
            if (method != ActivationMethod.UseGlobal)
            {
                return method == ActivationMethod.AttachThreadInput;
            }

            return AppConfig.DefaultActivationMethod == (int)ActivationMethod.AttachThreadInput;
        }

        /// <summary>
        /// Sync latest preset rule into current action settings when preset is referenced.
        /// </summary>
        private void SyncPresetIfNeeded()
        {
            if (_settings == null || string.IsNullOrEmpty(_settings.PresetId))
                return;

            var preset = WindowPresetManager.Instance.GetPresetById(_settings.PresetId);
            if (preset == null)
                return;

            if (_settings.WindowRule == null)
                _settings.WindowRule = new WindowRule();

            _settings.WindowRule.Name = preset.Name;
            _settings.WindowRule.ApplicationPath = preset.ApplicationPath;
            _settings.WindowRule.Conditions = preset.Conditions?.ToList();
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
