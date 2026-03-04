namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 窗口激活方式
    /// </summary>
    public enum ActivationMethod
    {
        /// <summary>
        /// 使用全局设置（默认）
        /// </summary>
        UseGlobal = 0,

        /// <summary>
        /// AttachThreadInput 模式：合并输入队列后激活，更可靠但可能导致 Chromium/Qt 应用卡死。
        /// </summary>
        AttachThreadInput = 1,

        /// <summary>
        /// 安全模式：仅使用 SetForegroundWindow + mouse_event + BringWindowToTop，不合并输入队列。
        /// 适用于 Chromium/Qt 等多进程应用。
        /// </summary>
        SafeMode = 2
    }
}
