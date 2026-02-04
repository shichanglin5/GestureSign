using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Controls;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Common.Localization;
using ManagedWinapi.Windows;

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

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetLastActivePopup(IntPtr hWnd);

        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const uint GA_ROOTOWNER = 3;
        private const int DWMWA_CLOAKED = 14;

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

                // Get current foreground window, fall back to gesture capture window
                IntPtr currentWindow = GetForegroundWindow();
                if (currentWindow == IntPtr.Zero)
                    currentWindow = ActionPoint.WindowHandle;
                if (currentWindow == IntPtr.Zero)
                    currentWindow = ApplicationManager.Instance.CaptureWindow?.HWnd ?? IntPtr.Zero;
                // When foreground is unknown, assume topmost window (Z-order first from EnumWindows) is current
                if (currentWindow == IntPtr.Zero)
                    currentWindow = windows[0];

                bool foregroundMinimized = currentWindow != IntPtr.Zero && IsIconic(currentWindow);
                // If foreground is minimized, restore it directly (like Alt+Tab)
                if (foregroundMinimized)
                {
                    ShowWindow(currentWindow, SW_RESTORE);
                    SystemWindow.ForegroundWindow = new SystemWindow(currentWindow);
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

                var nextTitle = GetWindowTitle(nextWindow);
                StringBuilder nextClass = new StringBuilder(256);
                GetClassName(nextWindow, nextClass, nextClass.Capacity);
                Logging.LogDebug($"[NextApplication] Switching: current=0x{currentWindow.ToString("X")} → next=0x{nextWindow.ToString("X")} '{nextTitle}' (class={nextClass}, index={nextIndex}/{windows.Count})");

                SystemWindow.ForegroundWindow = new SystemWindow(nextWindow);
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
            // Must be visible
            if (!IsWindowVisible(hWnd))
                return false;

            // Skip minimized windows only when configured to do so
            if (_settings.SkipMinimizedWindows && IsIconic(hWnd))
                return false;

            // Must have a title
            if (GetWindowTextLength(hWnd) == 0)
                return false;

            // Alt+Tab algorithm: walk to root owner, then check last active popup
            // See Raymond Chen's blog: "Which windows appear in the Alt+Tab list?"
            IntPtr rootOwner = GetAncestor(hWnd, GA_ROOTOWNER);
            if (rootOwner != IntPtr.Zero && GetLastActivePopup(rootOwner) != hWnd)
                return false;

            // Check extended window styles
            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // Skip tool windows unless they have WS_EX_APPWINDOW
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
                return false;

            // Skip non-activatable windows (not shown in Alt+Tab)
            if ((exStyle & WS_EX_NOACTIVATE) != 0)
                return false;

            // Skip DWM cloaked windows (e.g. hidden UWP system apps like TextInputHost)
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            // Skip shell internal windows (e.g. taskbar tab proxy windows)
            StringBuilder className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            if (className.ToString() == "Windows.Internal.Shell.TabProxyWindow")
                return false;

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
