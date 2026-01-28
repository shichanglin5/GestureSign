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
        private static Dictionary<string, IntPtr> _lastActivatedWindows = new();

        // Cache dictionary: Key = AUMID or ClassName+ProcessPath, Value = (WindowHandles, LastUpdateTime)
        private readonly Dictionary<string, (List<IntPtr> Handles, DateTime LastUpdate)> _windowCache = new();

        // Settings cache: Key = serializedData (JSON string), Value = deserialized settings
        // Avoids redundant JSON deserialization when configuration is unchanged
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
            // Settings cache (_settingsCache) will naturally invalidate when configuration changes
            // Window cache (_windowCache) uses complete matching configuration as key, so it naturally invalidates too
            // No need to clear caches manually - this allows per-application cache invalidation
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

                Logging.LogDebug($"[ActivateApp] Activating: {_settings.DisplayName ?? Path.GetFileNameWithoutExtension(_settings.ApplicationPath)}");

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
            // Use settings cache to avoid redundant JSON deserialization
            // When configuration is unchanged, the serializedData (JSON) will be identical
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
        /// Generate cache key from matching configuration
        /// Includes all parameters that affect window matching
        /// </summary>
        private string GenerateCacheKey(ActivateAppSettings settings)
        {
            // Include all matching criteria to ensure cache invalidates when any configuration changes
            return $"{settings.AUMID ?? ""}|{settings.WindowClassName ?? ""}|{settings.ApplicationPath ?? ""}|{settings.WindowTitlePattern ?? ""}|{settings.UseRegexMatching}";
        }

        private List<IntPtr> GetApplicationWindows(ActivateAppSettings settings)
        {
            var windows = new List<IntPtr>();

            // Strategy 1: If AUMID is configured, use AUMID matching (highest priority)
            if (!string.IsNullOrEmpty(settings.AUMID))
            {
                // Generate cache key from complete matching configuration
                // This ensures cache invalidates when ANY matching parameter changes
                string cacheKey = GenerateCacheKey(settings);

                // Cache strategy: only use cache when CacheExpirationSeconds > 0
                if (settings.CacheExpirationSeconds > 0 && _windowCache.TryGetValue(cacheKey, out var cachedData))
                {
                    var elapsed = (DateTime.Now - cachedData.LastUpdate).TotalSeconds;
                    if (elapsed <= settings.CacheExpirationSeconds)
                    {
                        // Cache is still valid - validate windows
                        var validWindows = new List<IntPtr>();

                        foreach (var hWnd in cachedData.Handles)
                        {
                            // Check if window still exists and is valid
                            if (IsWindow(hWnd) && IsSwitchableWindow(hWnd))
                            {
                                try
                                {
                                    // Verify AUMID still matches (window handle may be reused)
                                    string windowAUMID = GetWindowAUMID(hWnd);
                                    if (!string.IsNullOrEmpty(windowAUMID) &&
                                        string.Equals(windowAUMID, settings.AUMID, StringComparison.OrdinalIgnoreCase))
                                    {
                                        validWindows.Add(hWnd);
                                    }
                                }
                                catch
                                {
                                    // Access failed, skip this window
                                }
                            }
                        }

                        // If cache has valid windows, return them (avoid EnumWindows traversal)
                        if (validWindows.Count > 0)
                        {
                            // Update cache with valid windows
                            _windowCache[cacheKey] = (validWindows, DateTime.Now);
                            return validWindows;
                        }
                    }
                    else
                    {
                        // Cache expired
                    }

                    // Remove expired or invalid cache
                    _windowCache.Remove(cacheKey);
                }

                // Cache miss, disabled, or invalidated - perform full scan
                windows = GetWindowsByAUMID(settings.AUMID, settings);

                // Update cache (only when CacheExpirationSeconds > 0)
                if (settings.CacheExpirationSeconds > 0 && windows.Count > 0)
                {
                    _windowCache[cacheKey] = (new List<IntPtr>(windows), DateTime.Now);
                }

                return windows;
            }

            // Strategy 2: ClassName + ProcessPath matching
            if (!string.IsNullOrEmpty(settings.WindowClassName) &&
                !string.IsNullOrEmpty(settings.ApplicationPath))
            {
                // Generate cache key from complete matching configuration
                string cacheKey = GenerateCacheKey(settings);

                // Try to use cache first
                if (settings.CacheExpirationSeconds > 0 && _windowCache.TryGetValue(cacheKey, out var cachedData))
                {
                    var elapsed = (DateTime.Now - cachedData.LastUpdate).TotalSeconds;
                    if (elapsed <= settings.CacheExpirationSeconds)
                    {
                        // Validate cached windows
                        var validWindows = cachedData.Handles.Where(hWnd =>
                            IsWindow(hWnd) && IsSwitchableWindow(hWnd)).ToList();

                        if (validWindows.Count > 0)
                        {
                            _windowCache[cacheKey] = (validWindows, DateTime.Now);
                            return validWindows;
                        }
                    }

                    // Cache expired or invalid
                    _windowCache.Remove(cacheKey);
                }

                // Cache miss or disabled - perform full scan
                windows = GetWindowsByClassNameAndPath(settings);
                Logging.LogDebug($"[ActivateApp] Matched by ClassName + ProcessPath for {settings.DisplayName}: {windows.Count} windows found");

                // Update cache
                if (settings.CacheExpirationSeconds > 0 && windows.Count > 0)
                {
                    _windowCache[cacheKey] = (new List<IntPtr>(windows), DateTime.Now);
                }

                return windows;
            }

            // No valid configuration
            Logging.LogWarning($"[ActivateApp] No valid matching configuration for {settings.DisplayName}");
            return windows;
        }

        private List<IntPtr> GetWindowsByClassNameAndPath(ActivateAppSettings settings)
        {
            var windows = new List<IntPtr>();

            Logging.LogDebug($"[ActivateApp] Searching for ClassName='{settings.WindowClassName}', Path='{settings.ApplicationPath}'");

            EnumWindows((hWnd, lParam) =>
            {
                if (IsSwitchableWindow(hWnd))
                {
                    try
                    {
                        var window = new SystemWindow(hWnd);

                        // Check ClassName
                        if (window.ClassName != settings.WindowClassName)
                            return true; // Continue enumeration

                        // Check ProcessPath
                        GetWindowThreadProcessId(hWnd, out int pid);
                        var process = Process.GetProcessById(pid);
                        string processPath = null;

                        try
                        {
                            processPath = process.MainModule?.FileName;
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // Access denied - try matching by process name instead
                            string expectedFileName = Path.GetFileName(settings.ApplicationPath);
                            string actualFileName = process.ProcessName + ".exe";
                            if (string.Equals(expectedFileName, actualFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                processPath = settings.ApplicationPath; // Treat as match
                                Logging.LogDebug($"[ActivateApp] Process path access denied, matched by process name: {actualFileName}");
                            }
                        }

                        if (string.Equals(processPath, settings.ApplicationPath,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            // Apply title filters
                            if (MatchesFilters(hWnd, settings))
                            {
                                Logging.LogDebug($"[ActivateApp] Found matching window: 0x{hWnd:X} '{window.Title}'");
                                windows.Add(hWnd);
                            }
                        }
                    }
                    catch
                    {
                        // Process may have exited or access denied
                    }
                }
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        private bool MatchesFilters(IntPtr hWnd, ActivateAppSettings settings)
        {
            // Only apply title pattern filter (ClassName is now a core matching condition, not a filter)
            if (!string.IsNullOrEmpty(settings.WindowTitlePattern))
            {
                var window = new SystemWindow(hWnd);

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
            var windowState = window.WindowState;
            var foregroundWindow = GetForegroundWindow();
            bool isForeground = hWnd == foregroundWindow;

            // var fgWindow = new SystemWindow(foregroundWindow);
            // Logging.LogDebug($"[ActivateApp] HandleSingleWindow: hWnd=0x{hWnd:X}, state={windowState}, isForeground={isForeground}, title='{window.Title}', currentForeground=0x{foregroundWindow:X} '{fgWindow.Title}'");

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

            string appKey = _settings.ApplicationPath.ToLowerInvariant();
            IntPtr foreground = GetForegroundWindow();

            // Check if the current foreground window belongs to this application
            bool foregroundIsThisApp = windows.Contains(foreground);

            IntPtr targetWindow;

            if (foregroundIsThisApp)
            {
                // Already in this app — cycle to the next window
                int currentIndex = windows.IndexOf(foreground);
                int nextIndex = (currentIndex + 1) % windows.Count;
                targetWindow = windows[nextIndex];
                Logging.LogDebug($"[ActivateApp] Foreground is this app, cycling: index {currentIndex} -> {nextIndex}");
            }
            else
            {
                // Coming from another app — restore the last activated window
                _lastActivatedWindows.TryGetValue(appKey, out IntPtr lastWindow);

                // Validate last window still exists in the list
                if (lastWindow != IntPtr.Zero && windows.Contains(lastWindow))
                {
                    targetWindow = lastWindow;
                    Logging.LogDebug($"[ActivateApp] From other app, restoring last window: 0x{targetWindow:X}");
                }
                else
                {
                    // No valid last window — activate the topmost one
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

        #endregion

        #region AUMID Helper Methods

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore propertyStore);

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out uint count);
            [PreserveSig]
            int GetAt(uint iProp, out PropertyKey pkey);
            [PreserveSig]
            int GetValue(ref PropertyKey key, out PropVariant pv);
            [PreserveSig]
            int SetValue(ref PropertyKey key, ref PropVariant pv);
            [PreserveSig]
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;

            public PropertyKey(Guid fmtid, uint pid)
            {
                this.fmtid = fmtid;
                this.pid = pid;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pwszVal;
        }

        private string GetWindowAUMID(IntPtr hWnd)
        {
            try
            {
                Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
                PropertyKey PKEY_AppUserModel_ID = new PropertyKey(
                    new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

                int result = SHGetPropertyStoreForWindow(hWnd, ref iid, out IPropertyStore propertyStore);
                if (result != 0)
                    return null;

                try
                {
                    PropVariant pv;
                    result = propertyStore.GetValue(ref PKEY_AppUserModel_ID, out pv);
                    if (result != 0 || pv.vt != 31)
                        return null;

                    string aumid = Marshal.PtrToStringUni(pv.pwszVal);
                    return string.IsNullOrEmpty(aumid) ? null : aumid;
                }
                finally
                {
                    Marshal.ReleaseComObject(propertyStore);
                }
            }
            catch
            {
                return null;
            }
        }

        private List<IntPtr> GetWindowsByAUMID(string aumid, ActivateAppSettings settings)
        {
            var windows = new List<IntPtr>();

            EnumWindows((hWnd, lParam) =>
            {
                if (IsSwitchableWindow(hWnd))
                {
                    try
                    {
                        string windowAUMID = GetWindowAUMID(hWnd);

                        // AUMID matching (case-insensitive)
                        if (!string.IsNullOrEmpty(windowAUMID) &&
                            string.Equals(windowAUMID, aumid, StringComparison.OrdinalIgnoreCase))
                        {
                            // Apply additional filters (ClassName, TitlePattern)
                            if (MatchesFilters(hWnd, settings))
                            {
                                windows.Add(hWnd);
                            }
                        }
                    }
                    catch
                    {
                        // Access denied or other errors
                    }
                }
                return true;
            }, IntPtr.Zero);

            return windows;
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
