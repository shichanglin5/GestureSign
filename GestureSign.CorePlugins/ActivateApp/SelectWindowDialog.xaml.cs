using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;

namespace GestureSign.CorePlugins.ActivateApp
{
    public partial class SelectWindowDialog : Window
    {
        #region Private Variables

        private WindowInfo _selectedWindow;

        #endregion

        #region PInvoke Declarations

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private const uint GW_OWNER = 4;
        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;

        #endregion

        #region Constructor

        public SelectWindowDialog()
        {
            InitializeComponent();
            Loaded += SelectWindowDialog_Loaded;
        }

        #endregion

        #region Properties

        public WindowInfo SelectedWindow => _selectedWindow;

        #endregion

        #region Event Handlers

        private void SelectWindowDialog_Loaded(object sender, RoutedEventArgs e)
        {
            LoadRunningWindows();
        }

        private void WindowListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WindowListView.SelectedItem is WindowInfo windowInfo)
            {
                _selectedWindow = windowInfo;
                DialogResult = true;
                Close();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowListView.SelectedItem is WindowInfo windowInfo)
            {
                _selectedWindow = windowInfo;
                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion

        #region Private Methods

        private void LoadRunningWindows()
        {
            try
            {
                var windows = new List<WindowInfo>();

                EnumWindows((hWnd, lParam) =>
                {
                    if (IsSwitchableWindow(hWnd))
                    {
                        try
                        {
                            GetWindowThreadProcessId(hWnd, out int pid);
                            var process = Process.GetProcessById(pid);
                            var window = new SystemWindow(hWnd);

                            BitmapSource iconSource = null;
                            try
                            {
                                iconSource = Imaging.CreateBitmapSourceFromHIcon(
                                    window.Icon.Handle,
                                    Int32Rect.Empty,
                                    BitmapSizeOptions.FromEmptyOptions());
                                iconSource.Freeze();
                            }
                            catch
                            {
                                // Use default icon if extraction fails
                            }

                            var windowInfo = new WindowInfo
                            {
                                Handle = hWnd,
                                Title = window.Title,
                                ClassName = window.ClassName,
                                ProcessName = process.ProcessName,
                                ProcessPath = process.MainModule?.FileName ?? string.Empty,
                                Icon = iconSource
                            };

                            windows.Add(windowInfo);
                        }
                        catch
                        {
                            // Process may have exited or access denied
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                // Sort by process name, then by title
                windows = windows.OrderBy(w => w.ProcessName).ThenBy(w => w.Title).ToList();

                WindowListView.ItemsSource = windows;

                if (windows.Count > 0)
                {
                    WindowListView.SelectedIndex = 0;
                }

                Logging.LogDebug($"[SelectWindowDialog] Loaded {windows.Count} running windows");
            }
            catch (Exception ex)
            {
                Logging.LogError($"[SelectWindowDialog] Failed to load running windows: {ex.Message}");
                MessageBox.Show($"Failed to load running windows: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

            // Skip windows with an owner (child windows)
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

        #endregion
    }

    #region Helper Classes

    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public string ClassName { get; set; }
        public string ProcessName { get; set; }
        public string ProcessPath { get; set; }
        public BitmapSource Icon { get; set; }
    }

    #endregion
}
