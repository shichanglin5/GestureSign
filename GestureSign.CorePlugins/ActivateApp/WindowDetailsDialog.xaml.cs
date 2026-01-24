using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;

namespace GestureSign.CorePlugins.ActivateApp
{
    public partial class WindowDetailsDialog : Window
    {
        private IntPtr _windowHandle;
        private List<WindowPropertyInfo> _properties;

        public WindowDetailsDialog(IntPtr windowHandle)
        {
            InitializeComponent();
            _windowHandle = windowHandle;
            LoadWindowDetails();
        }

        private void LoadWindowDetails()
        {
            _properties = new List<WindowPropertyInfo>();

            try
            {
                var window = new SystemWindow(_windowHandle);

                // 获取进程信息
                int processId = window.ProcessId;
                Process process = null;

                try
                {
                    process = Process.GetProcessById(processId);
                }
                catch
                {
                    // Process may have exited
                }

                // === 1. 基础识别信息 ===
                AddProperty("Identification", "HWND (Handle)", _windowHandle.ToString("X"));
                AddProperty("Identification", "HWND (Decimal)", _windowHandle.ToString());
                AddProperty("Identification", "Window Title", window.Title);
                AddProperty("Identification", "Window Class Name", window.ClassName);

                if (process != null)
                {
                    AddProperty("Identification", "Process Name", process.ProcessName);
                    AddProperty("Identification", "Process ID", processId.ToString());
                    AddProperty("Identification", "Process Path", GetProcessPath(process));

                    // ⭐ 命令行参数 - 关键信息！
                    string commandLine = GetCommandLine(processId);
                    AddProperty("Identification", "Command Line", commandLine);

                    // ⭐ AUMID - 区分 UWP/PWA 应用的关键
                    string aumid = GetAUMID(_windowHandle);
                    AddProperty("Identification", "AUMID (AppUserModelID)", aumid);
                }

                // === 2. 窗口位置与大小 ===
                AddProperty("Position & Size", "Screen X", window.Position.Left.ToString());
                AddProperty("Position & Size", "Screen Y", window.Position.Top.ToString());
                AddProperty("Position & Size", "Width", window.Size.Width.ToString());
                AddProperty("Position & Size", "Height", window.Size.Height.ToString());
                AddProperty("Position & Size", "Client Area X", window.ClientRectangle.Left.ToString());
                AddProperty("Position & Size", "Client Area Y", window.ClientRectangle.Top.ToString());
                AddProperty("Position & Size", "Client Area Width", window.ClientRectangle.Width.ToString());
                AddProperty("Position & Size", "Client Area Height", window.ClientRectangle.Height.ToString());
                AddProperty("Position & Size", "Window State", window.WindowState.ToString());

                // === 3. 窗口可见性 ===
                AddProperty("Visibility", "Visible", window.Visible.ToString());
                AddProperty("Visibility", "Visibility Flag", window.VisibilityFlag.ToString());
                AddProperty("Visibility", "Enabled", window.Enabled.ToString());

                // === 4. 窗口样式 ===
                AddProperty("Styles", "Style Flags (Hex)", ((uint)window.Style).ToString("X8"));
                AddProperty("Styles", "Extended Style Flags (Hex)", ((uint)window.ExtendedStyle).ToString("X8"));
                AddProperty("Styles", "Is TopMost", window.TopMost.ToString());
                AddProperty("Styles", "Is Movable", window.Movable.ToString());
                AddProperty("Styles", "Is Resizable", window.Resizable.ToString());

                // === 5. 窗口层级 ===
                var parent = window.Parent;
                AddProperty("Hierarchy", "Parent HWND", parent != null ? parent.HWnd.ToString("X") : "(None)");

                var owner = GetWindowOwner(_windowHandle);
                AddProperty("Hierarchy", "Owner HWND", owner != IntPtr.Zero ? owner.ToString("X") : "(None)");

                AddProperty("Hierarchy", "Child Windows Count", window.AllChildWindows.Length.ToString());
                AddProperty("Hierarchy", "Z-Order Position", GetZOrder(_windowHandle).ToString());

                // === 6. 进程详细信息 ===
                if (process != null)
                {
                    try
                    {
                        AddProperty("Process Details", "Start Time", process.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                        AddProperty("Process Details", "Memory Usage (MB)", (process.WorkingSet64 / 1024 / 1024).ToString());
                        AddProperty("Process Details", "Thread Count", process.Threads.Count.ToString());
                        AddProperty("Process Details", "Handle Count", process.HandleCount.ToString());
                    }
                    catch
                    {
                        // Some properties may not be accessible
                    }
                }

                // 绑定到 DataGrid
                DetailsDataGrid.ItemsSource = _properties;
            }
            catch (Exception ex)
            {
                Logging.LogError($"[WindowDetailsDialog] Failed to load window details: {ex.Message}");

                // 添加错误信息到属性列表
                AddProperty("Error", "Failed to Load Details", ex.Message);
                AddProperty("Error", "Stack Trace", ex.StackTrace ?? "(none)");

                // 绑定错误信息到 DataGrid
                DetailsDataGrid.ItemsSource = _properties;
            }
        }

        private void AddProperty(string category, string name, string value)
        {
            _properties.Add(new WindowPropertyInfo
            {
                Category = category,
                Name = name,
                Value = value ?? "(null)"
            });
        }

        private string GetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? "(Unknown)";
            }
            catch
            {
                return "(Access Denied)";
            }
        }

        // ⭐ 获取命令行参数 - 区分 Edge PWA 的关键
        private string GetCommandLine(int processId)
        {
            try
            {
                string query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}";

                using (var searcher = new ManagementObjectSearcher(query))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        return obj["CommandLine"]?.ToString() ?? "(null)";
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogWarning($"[WindowDetailsDialog] Failed to get command line: {ex.Message}");
                return "(Failed to retrieve)";
            }

            return "(Not found)";
        }

        // ⭐ 获取 AUMID (Application User Model ID) - 区分 UWP/PWA 应用的关键
        private string GetAUMID(IntPtr hWnd)
        {
            try
            {
                Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"); // IID_IPropertyStore
                PropertyKey PKEY_AppUserModel_ID = new PropertyKey(
                    new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

                int result = SHGetPropertyStoreForWindow(hWnd, ref iid, out IPropertyStore propertyStore);
                if (result != 0)
                {
                    return "(Not available)";
                }

                try
                {
                    PropVariant pv;
                    result = propertyStore.GetValue(ref PKEY_AppUserModel_ID, out pv);
                    if (result != 0 || pv.vt != 31) // VT_LPWSTR = 31
                    {
                        return "(Not set)";
                    }

                    string aumid = Marshal.PtrToStringUni(pv.pwszVal);
                    return string.IsNullOrEmpty(aumid) ? "(Empty)" : aumid;
                }
                finally
                {
                    Marshal.ReleaseComObject(propertyStore);
                }
            }
            catch (Exception ex)
            {
                Logging.LogWarning($"[WindowDetailsDialog] Failed to get AUMID: {ex.Message}");
                return "(Failed to retrieve)";
            }
        }

        #region Helper Methods

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

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

        private const uint GW_OWNER = 4;
        private const uint GW_HWNDPREV = 3;

        private IntPtr GetWindowOwner(IntPtr hWnd)
        {
            return GetWindow(hWnd, GW_OWNER);
        }

        private int GetZOrder(IntPtr hWnd)
        {
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

        #region Event Handlers

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Window Details");
                sb.AppendLine("=".PadRight(80, '='));
                sb.AppendLine();

                string currentCategory = null;

                foreach (var prop in _properties)
                {
                    if (prop.Category != currentCategory)
                    {
                        currentCategory = prop.Category;
                        sb.AppendLine();
                        sb.AppendLine($"[{currentCategory}]");
                        sb.AppendLine("-".PadRight(80, '-'));
                    }

                    sb.AppendLine($"{prop.Name.PadRight(30)}: {prop.Value}");
                }

                // 重试机制：尝试多次设置剪贴板（处理 CLIPBRD_E_CANT_OPEN 错误）
                bool success = false;
                int maxRetries = 10;
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        if (i > 0)
                        {
                            Thread.Sleep(100 * i); // 递增延迟：100ms, 200ms, 300ms...
                        }

                        Clipboard.Clear();
                        Clipboard.SetText(sb.ToString());
                        success = true;
                        break;
                    }
                    catch (COMException ex) when (ex.HResult == unchecked((int)0x800401D0)) // CLIPBRD_E_CANT_OPEN
                    {
                        if (i == maxRetries - 1)
                        {
                            throw; // 最后一次重试失败，抛出异常
                        }
                        // 继续重试
                    }
                }

                if (success)
                {
                    MessageBox.Show("Window details copied to clipboard!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Logging.LogError($"[WindowDetailsDialog] Failed to copy to clipboard: {ex.Message}");
                MessageBox.Show($"Failed to copy: {ex.Message}\n\nThe clipboard may be locked by another application. Please try again.",
                    "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Data Model

        public class WindowPropertyInfo
        {
            public string Category { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
        }

        #endregion
    }
}
