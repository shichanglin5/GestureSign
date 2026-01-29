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
        private readonly Dictionary<string, ActivateAppSettings> _settingsCache = new();

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
                if (_settings?.DisplayName == null)
                    return LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.Description");

                return string.Format(
                    LocalizationProvider.Instance.GetTextValue("CorePlugins.ActivateApp.SpecificDescription"),
                    _settings.DisplayName);
            }
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

                // Log matching result
                var conditionInfo = string.Join(", ", _settings.MatchConditions.Select(c => $"{c.Type}={c.Value}"));
                Logging.LogDebug($"[ActivateApp] Activating: {_settings.DisplayName}, matched by [{conditionInfo}]: {appWindows.Count} windows found");

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
            // Use settings cache to avoid redundant JSON deserialization
            if (_settingsCache.TryGetValue(serializedData, out var cachedSettings))
            {
                _settings = cachedSettings;
                return true;
            }

            // Cache miss - deserialize and cache the result
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
                        _windowListCache[cacheKey] = (validWindows, DateTime.Now);
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
        /// Scan all windows and find matching ones using WindowMatcher
        /// </summary>
        private List<IntPtr> ScanMatchingWindows(ActivateAppSettings settings, bool includeHidden)
        {
            var windows = new List<IntPtr>();

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

                    // Use WindowMatcher's unified matching logic
                    if (WindowMatcher.MatchAllConditions(window, settings.MatchConditions))
                    {
                        windows.Add(hWnd);
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

            // Handle hidden window (tray app)
            if (!isVisible)
            {
                Logging.LogDebug($"[ActivateApp] Showing hidden window: 0x{hWnd:X} '{window.Title}'");
                ShowWindow(hWnd, SW_SHOW);
                window.RestoreWindow();
                SystemWindow.ForegroundWindow = window;
                return true;
            }

            var windowState = window.WindowState;

            // Check if window is minimized first
            if (windowState == FormWindowState.Minimized)
            {
                // Always restore and activate minimized windows
                window.RestoreWindow();
                SystemWindow.ForegroundWindow = window;
                return true;
            }

            // Window is not minimized, check if it's the foreground window
            if (isForeground)
            {
                // Window is already foreground and not minimized - minimize it
                window.WindowState = FormWindowState.Minimized;
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
        /// Try to launch application if ProcessPath condition is present
        /// </summary>
        private bool TryLaunchApplication(ActivateAppSettings settings)
        {
            // Find ProcessPath condition
            var pathCondition = settings.MatchConditions?
                .FirstOrDefault(c => c.Type == MatchConditionType.ProcessPath);

            if (pathCondition == null || string.IsNullOrEmpty(pathCondition.Value))
            {
                Logging.LogWarning($"[ActivateApp] No ProcessPath condition found, cannot launch application");
                return false;
            }

            string applicationPath = pathCondition.Value;

            try
            {
                if (!File.Exists(applicationPath))
                {
                    Logging.LogWarning($"[ActivateApp] Application not found: {applicationPath}");
                    return false;
                }

                Logging.LogDebug($"[ActivateApp] Launching application: {applicationPath}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = applicationPath,
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
