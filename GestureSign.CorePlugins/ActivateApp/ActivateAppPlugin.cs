using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
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

        // Dictionary to track last activated window per application path
        private static Dictionary<string, IntPtr> _lastActivatedWindows = new Dictionary<string, IntPtr>();

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

        private const uint GW_OWNER = 4;
        private const uint GW_HWNDPREV = 3;
        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;

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
            // No initialization needed
        }

        public bool Gestured(PointInfo actionPoint)
        {
            try
            {
                if (_settings == null || string.IsNullOrEmpty(_settings.ApplicationPath))
                {
                    Logging.LogDebug("[ActivateApp] No application configured");
                    return false;
                }

                // Step 1: Check if application is running
                var appWindows = GetApplicationWindows(_settings);

                if (appWindows.Count == 0)
                {
                    // Application not running - launch it
                    return LaunchApplication(_settings.ApplicationPath);
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
            return PluginHelper.DeserializeSettings(serializedData, out _settings);
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

        private List<IntPtr> GetApplicationWindows(ActivateAppSettings settings)
        {
            var windows = new List<IntPtr>();
            var processName = Path.GetFileNameWithoutExtension(settings.ApplicationPath);

            if (string.IsNullOrEmpty(processName))
                return windows;

            // Get all processes matching the process name
            var processes = Process.GetProcessesByName(processName);

            foreach (var process in processes)
            {
                try
                {
                    // Verify the process path matches (important for apps with common names)
                    var processPath = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(processPath))
                        continue;

                    // Path comparison (case-insensitive)
                    if (!string.Equals(processPath, settings.ApplicationPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Enumerate windows for this process
                    EnumWindows((hWnd, lParam) =>
                    {
                        GetWindowThreadProcessId(hWnd, out int pid);

                        if (pid == process.Id && IsSwitchableWindow(hWnd))
                        {
                            // Apply optional filters
                            if (MatchesFilters(hWnd, settings))
                            {
                                windows.Add(hWnd);
                            }
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                catch
                {
                    // Process may have exited
                    continue;
                }
            }

            return windows;
        }

        private bool MatchesFilters(IntPtr hWnd, ActivateAppSettings settings)
        {
            var window = new SystemWindow(hWnd);

            // Class name filter
            if (!string.IsNullOrEmpty(settings.WindowClassName))
            {
                if (window.ClassName != settings.WindowClassName)
                    return false;
            }

            // Title pattern filter
            if (!string.IsNullOrEmpty(settings.WindowTitlePattern))
            {
                if (settings.UseRegexMatching)
                {
                    if (!Regex.IsMatch(window.Title, settings.WindowTitlePattern, RegexOptions.IgnoreCase))
                        return false;
                }
                else
                {
                    if (!window.Title.Contains(settings.WindowTitlePattern, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            return true;
        }

        private bool HandleSingleWindow(IntPtr hWnd)
        {
            var window = new SystemWindow(hWnd);

            // Check if window is minimized first
            if (window.WindowState == FormWindowState.Minimized)
            {
                // Always restore and activate minimized windows
                window.RestoreWindow();
                SystemWindow.ForegroundWindow = window;
                return true;
            }

            // Window is not minimized, check if it's the foreground window
            var foregroundWindow = GetForegroundWindow();
            if (hWnd == foregroundWindow)
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

            IntPtr targetWindow = IntPtr.Zero;

            // Get last activated window for this app
            string appKey = _settings.ApplicationPath.ToLowerInvariant();
            IntPtr lastWindow = IntPtr.Zero;
            _lastActivatedWindows.TryGetValue(appKey, out lastWindow);

            // Validate last window still exists
            if (lastWindow != IntPtr.Zero && !windows.Contains(lastWindow))
            {
                lastWindow = IntPtr.Zero;
            }

            if (lastWindow == IntPtr.Zero)
            {
                // First activation or previous window closed - activate the topmost window
                targetWindow = windows[0];
            }
            else
            {
                // Find next window in cycle
                int currentIndex = windows.IndexOf(lastWindow);
                int nextIndex = (currentIndex + 1) % windows.Count;
                targetWindow = windows[nextIndex];
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

        private bool LaunchApplication(string applicationPath)
        {
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
            // Alt+Tab doesn't show owned windows
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

        private string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
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
