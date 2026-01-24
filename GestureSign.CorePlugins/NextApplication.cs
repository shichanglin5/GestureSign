using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Common.Localization;

#pragma warning disable CA1416 // Platform-specific API

namespace GestureSign.CorePlugins
{
    public class NextApplication : IPlugin
    {
        #region Private Variables

        IHostControl _HostControl = null;

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
            get { return null; }
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
                    return false;

                // Get current foreground window
                IntPtr currentWindow = GetForegroundWindow();

                // Find current window index
                int currentIndex = windows.IndexOf(currentWindow);

                // If current window is not in the list, start from beginning
                if (currentIndex == -1)
                {
                    Logging.LogDebug("[NextApplication] Current window not in switchable list, starting from first window");
                    currentIndex = -1; // Will become 0 after +1
                }

                // Get next window (cycle to first if at end)
                int nextIndex = (currentIndex + 1) % windows.Count;
                IntPtr nextWindow = windows[nextIndex];

                // Activate next window
                SetForegroundWindow(nextWindow);

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

            // Must not be minimized
            if (IsIconic(hWnd))
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
            return true;
            // Nothing to deserialize
        }

        public string Serialize()
        {
            // Nothing to serialize, send empty string
            return "";
        }

        public void ShowGUI(bool IsNew)
        {
            // Nothing to do here
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
