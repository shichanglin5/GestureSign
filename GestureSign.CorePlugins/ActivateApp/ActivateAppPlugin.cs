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

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll")]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll")]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        private const uint GW_OWNER = 4;
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;
        private const int SW_SHOW = 5;

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
                else if (appWindows.Count == 1)
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

                if (includeHidden && !IsWindow(hWnd))
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

        private bool HandleSingleWindow(IntPtr hWnd)
        {
            var window = new SystemWindow(hWnd);
            var foregroundWindow = GetForegroundWindow();
            bool isForeground = hWnd == foregroundWindow;
            bool isVisible = IsWindowVisible(hWnd);
            var windowState = window.WindowState;

            Logging.LogDebug($"[ActivateApp] HandleSingleWindow: target=0x{hWnd:X}, foreground=0x{foregroundWindow:X}, isForeground={isForeground}, isVisible={isVisible}, windowState={windowState}");

            // Handle hidden window (tray app)
            if (!isVisible)
            {
                Logging.LogDebug($"[ActivateApp] Showing hidden window: 0x{hWnd:X} '{window.Title}'");
                ShowWindow(hWnd, SW_SHOW);
                window.RestoreWindow();
                SystemWindow.ForegroundWindow = window;
                return true;
            }

            // Check if window is minimized first
            if (windowState == FormWindowState.Minimized)
            {
                window.RestoreWindow();
                SystemWindow.ForegroundWindow = window;
                return true;
            }

            // If we're not the foreground, check if the foreground window's process is
            // a child/parent of the target's process (e.g. WeChatAppEx is a child process
            // of Weixin). This reliably detects related processes regardless of window title.
            if (!isForeground && foregroundWindow != IntPtr.Zero && IsProcessRelated(foregroundWindow, hWnd))
            {
                Logging.LogDebug($"[ActivateApp] Foreground 0x{foregroundWindow:X} is related process to target 0x{hWnd:X}, treating as foreground");
                isForeground = true;
            }

            // Window is not minimized, check if it's the foreground window
            if (isForeground)
            {
                // Window is already foreground and not minimized
                if (_settings.MinimizeIfActivated)
                {
                    // Minimize it
                    window.WindowState = FormWindowState.Minimized;
                }
                return true;
            }
            else
            {
                // Window is background - activate it
                SystemWindow.ForegroundWindow = window;
                return true;
            }
        }

        private bool HandleMultiWindow(List<IntPtr> windows)
        {
            // Sort windows by Z-order (most recently activated first)
            windows = windows.OrderByDescending(w => GetWindowZOrder(w)).ToList();

            string appKey = _settings.GenerateCacheKey();
            IntPtr foreground = GetForegroundWindow();

            // Check if the current foreground window belongs to this application
            bool foregroundIsThisApp = windows.Contains(foreground);

            IntPtr targetWindow;

            if (foregroundIsThisApp)
            {
                // Already in this app - cycle to the next window
                int currentIndex = windows.IndexOf(foreground);
                int nextIndex = (currentIndex + 1) % windows.Count;
                targetWindow = windows[nextIndex];
                // Logging.LogDebug($"[ActivateApp] Foreground is this app, cycling: index {currentIndex} -> {nextIndex}");
            }
            else
            {
                // Coming from another app - restore the last activated window
                _lastActivatedWindows.TryGetValue(appKey, out IntPtr lastWindow);

                // Validate last window still exists in the list
                if (lastWindow != IntPtr.Zero && windows.Contains(lastWindow))
                {
                    targetWindow = lastWindow;
                    Logging.LogDebug($"[ActivateApp] From other app, restoring last window: 0x{targetWindow:X}");
                }
                else
                {
                    // No valid last window - activate the topmost one
                    targetWindow = windows[0];
                    Logging.LogDebug($"[ActivateApp] From other app, no last window, using topmost: 0x{targetWindow:X}");
                }
            }

            // Activate the target window
            var window = new SystemWindow(targetWindow);
            bool isVisible = IsWindowVisible(targetWindow);

            // Handle hidden window (tray app)
            if (!isVisible)
            {
                Logging.LogDebug($"[ActivateApp] Showing hidden window: 0x{targetWindow:X} '{window.Title}'");
                ShowWindow(targetWindow, SW_SHOW);
                window.RestoreWindow();
                SystemWindow.ForegroundWindow = window;
                _lastActivatedWindows[appKey] = targetWindow;
                return true;
            }

            if (window.WindowState == FormWindowState.Minimized)
            {
                window.RestoreWindow();
            }

            SystemWindow.ForegroundWindow = window;

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

            return true;
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
        /// Check if two windows' processes have a parent-child relationship
        /// with different executable names. Same-exe processes (e.g. multiple
        /// msedge.exe instances for browser tabs and PWAs) are NOT considered
        /// related, as they represent independent application windows.
        /// </summary>
        private bool IsProcessRelated(IntPtr hwnd1, IntPtr hwnd2)
        {
            GetWindowThreadProcessId(hwnd1, out int pid1);
            GetWindowThreadProcessId(hwnd2, out int pid2);

            if (pid1 == pid2) return true;
            if (pid1 == 0 || pid2 == 0) return false;

            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return false;

            try
            {
                var processes = new Dictionary<uint, (uint parentId, string exeName)>();
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };

                if (Process32First(snapshot, ref entry))
                {
                    do
                    {
                        processes[entry.th32ProcessID] = (entry.th32ParentProcessID, entry.szExeFile);
                    } while (Process32Next(snapshot, ref entry));
                }

                // Get exe names for both processes
                string exe1 = processes.TryGetValue((uint)pid1, out var info1) ? info1.exeName : null;
                string exe2 = processes.TryGetValue((uint)pid2, out var info2) ? info2.exeName : null;

                // Same executable name means independent instances (e.g. msedge.exe tabs/PWAs),
                // not a host-embedded relationship — skip parent chain check
                if (exe1 != null && exe2 != null &&
                    string.Equals(exe1, exe2, StringComparison.OrdinalIgnoreCase))
                    return false;

                // Check if pid1 is ancestor of pid2
                uint current = (uint)pid2;
                for (int depth = 0; depth < 10 && current != 0; depth++)
                {
                    if (current == (uint)pid1) return true;
                    if (!processes.TryGetValue(current, out var p)) break;
                    current = p.parentId;
                }

                // Check if pid2 is ancestor of pid1
                current = (uint)pid1;
                for (int depth = 0; depth < 10 && current != 0; depth++)
                {
                    if (current == (uint)pid2) return true;
                    if (!processes.TryGetValue(current, out var p)) break;
                    current = p.parentId;
                }
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return false;
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
