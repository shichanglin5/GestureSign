using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Controls;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Common.Localization;

#pragma warning disable CA1416 // Platform-specific API

namespace GestureSign.CorePlugins
{
    public class NextApplicationSettings
    {
        public bool SkipMinimizedWindows { get; set; } = true;
    }

    public class NextApplication : IPlugin
    {
        #region Private Variables

        IHostControl _HostControl = null;
        NextApplicationSettings _settings = new NextApplicationSettings();
        NextApplicationUI _gui;

        #endregion

        #region PInvoke Declarations

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;
        private const uint GW_OWNER = 4;

        #endregion

        #region IAction Properties

        public string Name
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.NextApplication.Name"); }
        }

        public string Description
        {
            get { return LocalizationProvider.Instance.GetTextValue("CorePlugins.NextApplication.Description"); }
        }

        public object GUI
        {
            get
            {
                if (_gui == null)
                    _gui = new NextApplicationUI();
                return _gui;
            }
        }

        public bool ActivateWindowDefault
        {
            get { return false; }
        }

        public string Category
        {
            get { return "Windows"; }
        }

        public bool IsAction
        {
            get { return true; }
        }

        public object Icon => IconSource.Window;

        #endregion

        #region IAction Methods

        public void Initialize()
        {

        }

        public bool Gestured(PointInfo ActionPoint)
        {
            try
            {
                // Get all switchable windows
                var windows = GetSwitchableWindows();
                if (windows.Count <= 1)
                {
                    return false;
                }

                // Get current foreground window
                IntPtr currentWindow = GetForegroundWindow();
                bool foregroundMinimized = IsIconic(currentWindow);
                // If foreground is minimized, restore it directly (like Alt+Tab)
                if (foregroundMinimized)
                {
                    ShowWindow(currentWindow, SW_RESTORE);
                    SetForegroundWindow(currentWindow);
                    return true;
                }

                // Find current window index
                int currentIndex = windows.IndexOf(currentWindow);
                // If current window is not in the list, start from beginning
                if (currentIndex < 0)
                {
                    currentIndex = -1; // Will become 0 after +1
                }

                // Get next window (cycle to first if at end)
                int nextIndex = (currentIndex + 1) % windows.Count;
                IntPtr nextWindow = windows[nextIndex];

                // Activate next window (restore if minimized)
                if (IsIconic(nextWindow))
                {
                    ShowWindow(nextWindow, SW_RESTORE);
                }
                bool result = SetForegroundWindow(nextWindow);
                return true;
            }
            catch (Exception ex)
            {
                Logging.LogError($"[NextApplication] Error: {ex.Message}");
                return false;
            }
        }

        private List<IntPtr> GetSwitchableWindows()
        {
            var windows = new List<IntPtr>();

            EnumWindows((hWnd, lParam) =>
            {
                if (IsSwitchableWindow(hWnd))
                {
                    windows.Add(hWnd);
                }
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        private bool IsSwitchableWindow(IntPtr hWnd)
        {
            bool visible = IsWindowVisible(hWnd);
            bool iconic = IsIconic(hWnd);

            // Must be visible (always required — invisible windows are background/internal windows)
            if (!visible)
                return false;

            // Skip minimized windows only when configured to do so
            if (_settings.SkipMinimizedWindows && iconic)
                return false;

            // Must have a title
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return false;

            string title = GetWindowTitle(hWnd);

            // Skip windows with an owner (child windows like SubWebView)
            // Alt+Tab doesn't show owned windows
            IntPtr ownerWindow = GetWindow(hWnd, GW_OWNER);
            if (ownerWindow != IntPtr.Zero)
            {
                return false;
            }

            // Check extended window styles
            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // Skip tool windows unless they have WS_EX_APPWINDOW
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
            {
                return false;
            }
            return true;
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

        public bool Deserialize(string SerializedData)
        {
            if (string.IsNullOrEmpty(SerializedData))
            {
                _settings = new NextApplicationSettings();
                return true;
            }
            return PluginHelper.DeserializeSettings(SerializedData, out _settings);
        }

        public string Serialize()
        {
            if (_gui != null)
            {
                _settings.SkipMinimizedWindows = _gui.SkipMinimizedWindows;
            }
            return PluginHelper.SerializeSettings(_settings);
        }

        public void ShowGUI(bool IsNew)
        {
            if (_gui == null)
                _gui = new NextApplicationUI();
            _gui.SkipMinimizedWindows = _settings.SkipMinimizedWindows;
        }

        #endregion

        #region Host Control

        public IHostControl HostControl
        {
            get { return _HostControl; }
            set { _HostControl = value; }
        }

        #endregion
    }
}
