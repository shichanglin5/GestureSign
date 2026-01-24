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

        #region AUMID Retrieval

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
                    if (result != 0 || pv.vt != 31) // VT_LPWSTR = 31
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
                // Automatically extract AUMID
                try
                {
                    string aumid = GetWindowAUMID(windowInfo.Handle);
                    if (!string.IsNullOrEmpty(aumid))
                    {
                        windowInfo.AUMID = aumid;
                    }
                }
                catch (Exception ex)
                {
                    Logging.LogWarning($"[SelectWindowDialog] Failed to get AUMID: {ex.Message}");
                }

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

        private void DetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowListView.SelectedItem is WindowInfo selectedWindow)
            {
                try
                {
                    // 创建并显示窗口详细信息对话框
                    var detailsDialog = new WindowDetailsDialog(selectedWindow.Handle)
                    {
                        Owner = this
                    };

                    detailsDialog.ShowDialog();
                }
                catch (Exception ex)
                {
                    Logging.LogError($"[SelectWindowDialog] Failed to show window details: {ex.Message}");
                    MessageBox.Show(
                        $"Failed to show window details: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(
                    "Please select a window first.",
                    "No Window Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
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
        public string AUMID { get; set; }
    }

    #endregion
}
