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
        /// 进程 ID（用于延迟加载命令行）
        /// </summary>
        public int ProcessId { get; set; }

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
        /// 进程完整命令行（包含可执行文件路径和参数）
        /// </summary>
        public string CommandLine { get; set; }

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

        /// <summary>
        /// 从命令行中提取参数（去除可执行文件路径部分）
        /// </summary>
        public string GetArguments()
        {
            if (string.IsNullOrEmpty(CommandLine))
                return string.Empty;

            string cmdLine = CommandLine.Trim();

            // 命令行格式：
            // 1. "path with spaces\app.exe" arg1 arg2
            // 2. path\app.exe arg1 arg2

            if (cmdLine.StartsWith("\""))
            {
                // 带引号的路径
                int endQuote = cmdLine.IndexOf('"', 1);
                if (endQuote > 0 && endQuote < cmdLine.Length - 1)
                {
                    return cmdLine.Substring(endQuote + 1).TrimStart();
                }
                return string.Empty;
            }
            else
            {
                // 不带引号的路径，以第一个空格分隔
                int spaceIndex = cmdLine.IndexOf(' ');
                if (spaceIndex > 0)
                {
                    return cmdLine.Substring(spaceIndex + 1).TrimStart();
                }
                return string.Empty;
            }
        }
    }
}
