using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GestureSign.Common.Applications;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;

namespace GestureSign.Common.UI
{
    /// <summary>
    /// 统一的窗口选择对话框，支持十字准星、窗口列表、实时追踪
    /// </summary>
    public partial class WindowSelectorDialog : Window
    {
        #region Private Fields

        private WindowMatchInfo _selectedWindow;
        private DispatcherTimer _trackingTimer;
        private bool _isTracking = false;
        private IntPtr _selfHandle;

        #endregion

        #region PInvoke Declarations

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

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

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private const uint GW_OWNER = 4;
        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;

        #endregion

        #region Constructor

        public WindowSelectorDialog()
        {
            InitializeComponent();
        }

        #endregion

        #region Properties

        /// <summary>
        /// 获取选中的窗口信息
        /// </summary>
        public WindowMatchInfo SelectedWindow => _selectedWindow;

        #endregion

        #region Event Handlers

        private void WindowSelectorDialog_Loaded(object sender, RoutedEventArgs e)
        {
            _selfHandle = new WindowInteropHelper(this).Handle;
            LoadRunningWindows();
            UpdateTrackingButtonText();
        }

        private void WindowSelectorDialog_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                ToggleTracking();
                e.Handled = true;
            }
        }

        private void WindowListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WindowListView.SelectedItem is WindowMatchInfo windowInfo)
            {
                _selectedWindow = windowInfo;
                UpdateWindowInfoDisplay(windowInfo);
            }
        }

        private void WindowListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WindowListView.SelectedItem is WindowMatchInfo windowInfo)
            {
                _selectedWindow = windowInfo;

                // 延迟获取命令行
                if (string.IsNullOrEmpty(_selectedWindow.CommandLine) && _selectedWindow.ProcessId > 0)
                {
                    _selectedWindow.CommandLine = GetProcessCommandLine(_selectedWindow.ProcessId);
                }

                DialogResult = true;
                Close();
            }
        }

        private void chCrosshair_CrosshairDragging(object sender, MouseEventArgs e)
        {
            try
            {
                var point = PointToScreen(e.GetPosition(this));
                var window = SystemWindow.FromPointEx((int)point.X, (int)point.Y, false, false);
                if (window != null && window.HWnd != _selfHandle)
                {
                    var windowInfo = GetWindowMatchInfo(window.HWnd);
                    if (windowInfo != null)
                    {
                        _selectedWindow = windowInfo;
                        UpdateWindowInfoDisplay(windowInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[WindowSelectorDialog] Crosshair dragging error: {ex.Message}");
            }
        }

        private void chCrosshair_CrosshairDragged(object sender, MouseButtonEventArgs e)
        {
            // 拖拽结束，窗口信息已在 dragging 中更新
        }

        private void IncludeChildWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            LoadRunningWindows();
        }

        private void TrackingButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTracking();
        }

        private void DetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedWindow == null || _selectedWindow.Handle == IntPtr.Zero)
                return;

            var dialog = new WindowDetailsDialog { Owner = this };
            dialog.LoadWindowInfo(_selectedWindow.Handle);
            dialog.ShowDialog();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedWindow == null) return;

            var info = $"Title: {_selectedWindow.Title}\n" +
                       $"ClassName: {_selectedWindow.ClassName}\n" +
                       $"ProcessName: {_selectedWindow.ProcessName}\n" +
                       $"ProcessPath: {_selectedWindow.ProcessPath}\n" +
                       $"AUMID: {_selectedWindow.AUMID ?? "(None)"}\n" +
                       $"Handle: 0x{_selectedWindow.Handle.ToString("X")}";

            Clipboard.SetText(info);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadRunningWindows();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedWindow != null)
            {
                // 确保获取 AUMID
                if (string.IsNullOrEmpty(_selectedWindow.AUMID))
                {
                    _selectedWindow.AUMID = WindowMatcher.GetWindowAUMID(_selectedWindow.Handle);
                }

                // 注意：CommandLine 已改为延迟加载，不在此处查询
                // 如需 CommandLine，调用方应在必要时自行调用 GetProcessCommandLine

                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }
            Close();
        }

        /// <summary>
        /// 公开的方法，允许调用方按需获取命令行（WMI 查询较慢）
        /// </summary>
        public void LoadCommandLineIfNeeded()
        {
            if (_selectedWindow != null &&
                string.IsNullOrEmpty(_selectedWindow.CommandLine) &&
                _selectedWindow.ProcessId > 0)
            {
                _selectedWindow.CommandLine = GetProcessCommandLine(_selectedWindow.ProcessId);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion

        #region Tracking Methods

        private void ToggleTracking()
        {
            if (_isTracking)
                StopTracking();
            else
                StartTracking();
        }

        private void StartTracking()
        {
            _isTracking = true;
            UpdateTrackingButtonText();

            TrackingStatusBorder.Visibility = Visibility.Visible;
            TrackingStatusText.Text = LocalizationProvider.Instance.GetTextValue("WindowSelector.TrackingActive");

            _trackingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _trackingTimer.Tick += TrackingTimer_Tick;
            _trackingTimer.Start();
        }

        private void StopTracking()
        {
            _isTracking = false;
            _trackingTimer?.Stop();
            _trackingTimer = null;
            UpdateTrackingButtonText();

            TrackingStatusBorder.Visibility = Visibility.Collapsed;
        }

        private void TrackingTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow != _selfHandle && foregroundWindow != IntPtr.Zero)
                {
                    var windowInfo = GetWindowMatchInfo(foregroundWindow);
                    if (windowInfo != null && windowInfo.Handle != _selectedWindow?.Handle)
                    {
                        _selectedWindow = windowInfo;
                        UpdateWindowInfoDisplay(windowInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[WindowSelectorDialog] Tracking error: {ex.Message}");
            }
        }

        private void UpdateTrackingButtonText()
        {
            if (_isTracking)
            {
                TrackingButton.Content = LocalizationProvider.Instance.GetTextValue("WindowSelector.StopTracking") + " (F2)";
            }
            else
            {
                TrackingButton.Content = LocalizationProvider.Instance.GetTextValue("WindowSelector.StartTracking") + " (F2)";
            }
        }

        #endregion

        #region Window Loading Methods

        private void LoadRunningWindows()
        {
            try
            {
                var windows = new List<WindowMatchInfo>();
                bool includeChildren = IncludeChildWindowsCheckBox?.IsChecked == true;

                EnumWindows((hWnd, lParam) =>
                {
                    if (IsSwitchableWindow(hWnd))
                    {
                        var windowInfo = GetWindowMatchInfo(hWnd);
                        if (windowInfo != null)
                        {
                            windows.Add(windowInfo);

                            // 如果包含子窗口
                            if (includeChildren)
                            {
                                EnumChildWindows(hWnd, (childHwnd, childLParam) =>
                                {
                                    if (IsWindowVisible(childHwnd))
                                    {
                                        var childInfo = GetWindowMatchInfo(childHwnd);
                                        if (childInfo != null && !string.IsNullOrEmpty(childInfo.ClassName))
                                        {
                                            childInfo.Title = $"  ↳ {childInfo.Title}"; // 标记为子窗口
                                            windows.Add(childInfo);
                                        }
                                    }
                                    return true;
                                }, IntPtr.Zero);
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                // 按进程名排序
                windows = windows.OrderBy(w => w.ProcessName).ThenBy(w => w.Title).ToList();

                WindowListView.ItemsSource = windows;

                if (windows.Count > 0)
                {
                    WindowListView.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[WindowSelectorDialog] Failed to load windows: {ex.Message}");
            }
        }

        private bool IsSwitchableWindow(IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd))
                return false;

            if (GetWindowTextLength(hWnd) == 0)
                return false;

            IntPtr owner = GetWindow(hWnd, GW_OWNER);
            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // 排除工具窗口（除非是应用窗口）
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
                return false;

            // 排除有所有者的窗口（子窗口会单独处理）
            if (owner != IntPtr.Zero)
                return false;

            // 排除自己
            if (hWnd == _selfHandle)
                return false;

            return true;
        }

        private WindowMatchInfo GetWindowMatchInfo(IntPtr hWnd)
        {
            try
            {
                var window = new SystemWindow(hWnd);
                GetWindowThreadProcessId(hWnd, out int pid);
                var process = Process.GetProcessById(pid);

                BitmapSource iconSource = null;
                try
                {
                    if (window.Icon != null)
                    {
                        iconSource = Imaging.CreateBitmapSourceFromHIcon(
                            window.Icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        iconSource.Freeze();
                    }
                }
                catch { }

                // 使用 WindowMatcher 获取进程路径和 AUMID
                string processPath = WindowMatcher.GetProcessPath(hWnd, out string processName);
                string aumid = WindowMatcher.GetWindowAUMID(hWnd);

                // 注意：CommandLine 延迟加载，避免 WMI 查询影响性能
                return new WindowMatchInfo
                {
                    Handle = hWnd,
                    Title = window.Title ?? string.Empty,
                    ClassName = window.ClassName ?? string.Empty,
                    ProcessName = processName ?? process.ProcessName,
                    ProcessPath = processPath ?? string.Empty,
                    AUMID = aumid,
                    ProcessId = pid,
                    Icon = iconSource
                };
            }
            catch (Exception ex)
            {
                Logging.LogTrace($"[WindowSelectorDialog] Failed to get window info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 通过 WMI 获取进程的命令行参数
        /// </summary>
        private string GetProcessCommandLine(int processId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var cmdLine = obj["CommandLine"]?.ToString();
                    if (!string.IsNullOrEmpty(cmdLine))
                    {
                        return cmdLine;
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogTrace($"[WindowSelectorDialog] Failed to get command line for PID {processId}: {ex.Message}");
            }
            return string.Empty;
        }

        private void UpdateWindowInfoDisplay(WindowMatchInfo info)
        {
            if (info == null) return;

            TitleTextBox.Text = info.Title ?? string.Empty;
            ClassNameTextBox.Text = info.ClassName ?? string.Empty;
            ProcessNameTextBox.Text = info.ProcessName ?? string.Empty;
            ProcessPathTextBox.Text = info.ProcessPath ?? string.Empty;
            AUMIDTextBox.Text = info.AUMID ?? "(None)";
            HandleTextBox.Text = $"0x{info.Handle.ToString("X")}";
        }

        #endregion

        #region Cleanup

        protected override void OnClosed(EventArgs e)
        {
            StopTracking();
            base.OnClosed(e);
        }

        #endregion
    }
}
