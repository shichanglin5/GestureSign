using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using GestureSign.Common.Applications;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;

namespace GestureSign.Common.UI
{
    /// <summary>
    /// 窗口详细信息对话框
    /// </summary>
    public partial class WindowDetailsDialog : Window
    {
        #region PInvoke

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public int rcCaretLeft;
            public int rcCaretTop;
            public int rcCaretRight;
            public int rcCaretBottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const uint GW_OWNER = 4;

        #endregion

        public WindowDetailsDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 加载并显示指定窗口的详细信息
        /// </summary>
        public void LoadWindowInfo(IntPtr hWnd)
        {
            try
            {
                var window = new SystemWindow(hWnd);
                GetWindowThreadProcessId(hWnd, out int pid);

                // 基本信息
                TitleTextBox.Text = window.Title ?? string.Empty;
                ClassNameTextBox.Text = window.ClassName ?? string.Empty;
                HandleTextBox.Text = $"0x{hWnd.ToString("X")}";

                // 进程信息
                string processPath = WindowMatcher.GetProcessPath(hWnd, out string processName);
                string aumid = WindowMatcher.GetWindowAUMID(hWnd);
                ProcessNameTextBox.Text = processName ?? string.Empty;
                ProcessIdTextBox.Text = pid.ToString();
                ProcessPathTextBox.Text = processPath ?? string.Empty;
                AUMIDTextBox.Text = aumid ?? "(None)";

                // 窗口属性
                if (GetWindowRect(hWnd, out RECT rect))
                {
                    WindowRectTextBox.Text = $"({rect.Left}, {rect.Top}) {rect.Right - rect.Left}x{rect.Bottom - rect.Top}";
                }

                uint style = GetWindowLong(hWnd, GWL_STYLE);
                uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                WindowStyleTextBox.Text = $"0x{style:X8}";
                ExtendedStyleTextBox.Text = $"0x{exStyle:X8}";

                // 焦点控件信息
                LoadFocusInfo(hWnd);

                // 父/所有者窗口
                IntPtr parentHwnd = GetParent(hWnd);
                IntPtr ownerHwnd = GetWindow(hWnd, GW_OWNER);
                ParentHandleTextBox.Text = parentHwnd != IntPtr.Zero ? $"0x{parentHwnd.ToString("X")}" : "(None)";
                OwnerHandleTextBox.Text = ownerHwnd != IntPtr.Zero ? $"0x{ownerHwnd.ToString("X")}" : "(None)";
            }
            catch (Exception ex)
            {
                Logging.LogError($"[WindowDetailsDialog] Failed to load window info: {ex.Message}");
            }
        }

        private void LoadFocusInfo(IntPtr hWnd)
        {
            var info = new GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(info);
            uint threadId = (uint)GetWindowThreadProcessId(hWnd, out _);

            if (GetGUIThreadInfo(threadId, ref info))
            {
                FocusHandleTextBox.Text = info.hwndFocus != IntPtr.Zero
                    ? $"0x{info.hwndFocus.ToString("X")}"
                    : "(None)";

                if (info.hwndFocus != IntPtr.Zero)
                {
                    var sb = new StringBuilder(256);
                    if (GetClassName(info.hwndFocus, sb, sb.Capacity) > 0)
                        FocusClassNameTextBox.Text = sb.ToString();
                    else
                        FocusClassNameTextBox.Text = "(Unknown)";
                }
                else
                {
                    FocusClassNameTextBox.Text = "(None)";
                }

                if (info.hwndCaret != IntPtr.Zero)
                {
                    string relation = info.hwndCaret == info.hwndFocus
                        ? " (= Focus)"
                        : " (≠ Focus)";
                    CaretHandleTextBox.Text = $"0x{info.hwndCaret.ToString("X")}{relation}";
                }
                else
                {
                    CaretHandleTextBox.Text = "(None)";
                }
            }
            else
            {
                FocusHandleTextBox.Text = "(Failed)";
                FocusClassNameTextBox.Text = "(Failed)";
                CaretHandleTextBox.Text = "(Failed)";
            }

            // 文本输入框检测
            bool isTextInputWin32 = WindowMatcher.DetectFocusedTextInput(hWnd, useUIA: false);
            IsTextInputTextBox.Text = isTextInputWin32.ToString();

            bool isTextInputUIA = WindowMatcher.DetectFocusedTextInput(hWnd, useUIA: true);
            IsTextInputUIATextBox.Text = isTextInputUIA.ToString();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Title: {TitleTextBox.Text}");
            sb.AppendLine($"ClassName: {ClassNameTextBox.Text}");
            sb.AppendLine($"Handle: {HandleTextBox.Text}");
            sb.AppendLine($"ProcessName: {ProcessNameTextBox.Text}");
            sb.AppendLine($"ProcessId: {ProcessIdTextBox.Text}");
            sb.AppendLine($"ProcessPath: {ProcessPathTextBox.Text}");
            sb.AppendLine($"AUMID: {AUMIDTextBox.Text}");
            sb.AppendLine($"WindowRect: {WindowRectTextBox.Text}");
            sb.AppendLine($"Style: {WindowStyleTextBox.Text}");
            sb.AppendLine($"ExStyle: {ExtendedStyleTextBox.Text}");
            sb.AppendLine($"FocusHandle: {FocusHandleTextBox.Text}");
            sb.AppendLine($"FocusClassName: {FocusClassNameTextBox.Text}");
            sb.AppendLine($"CaretHandle: {CaretHandleTextBox.Text}");
            sb.AppendLine($"IsTextInput(Win32): {IsTextInputTextBox.Text}");
            sb.AppendLine($"IsTextInput(UIA): {IsTextInputUIATextBox.Text}");
            sb.AppendLine($"ParentHandle: {ParentHandleTextBox.Text}");
            sb.AppendLine($"OwnerHandle: {OwnerHandleTextBox.Text}");

            Clipboard.SetText(sb.ToString());
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
