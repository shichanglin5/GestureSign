using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace GestureSign.Common.UI
{
    /// <summary>
    /// 统一的窗口匹配信息模型，用于窗口选择对话框返回值
    /// </summary>
    public class WindowMatchInfo
    {
        /// <summary>
        /// 窗口句柄
        /// </summary>
        public IntPtr Handle { get; set; }

        /// <summary>
        /// 窗口标题
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 窗口类名
        /// </summary>
        public string ClassName { get; set; }

        /// <summary>
        /// 进程名（不含路径）
        /// </summary>
        public string ProcessName { get; set; }

        /// <summary>
        /// 进程完整路径
        /// </summary>
        public string ProcessPath { get; set; }

        /// <summary>
        /// Application User Model ID（用于 UWP/PWA 应用）
        /// </summary>
        public string AUMID { get; set; }

        /// <summary>
        /// 应用程序图标
        /// </summary>
        public BitmapSource Icon { get; set; }

        /// <summary>
        /// 获取文件名（从 ProcessPath 或 ProcessName）
        /// </summary>
        public string FileName => string.IsNullOrEmpty(ProcessPath)
            ? ProcessName
            : Path.GetFileName(ProcessPath);

        /// <summary>
        /// 是否有有效的窗口信息
        /// </summary>
        public bool IsValid => Handle != IntPtr.Zero && !string.IsNullOrEmpty(ClassName);
    }
}
