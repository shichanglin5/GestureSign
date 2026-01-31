namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 鼠标位置窗口检测模式
    /// </summary>
    public enum MouseWindowDetectionMode
    {
        /// <summary>
        /// 使用全局配置（仅对 UserApp 有效）
        /// </summary>
        Default = 0,

        /// <summary>
        /// 启用：鼠标移动后使用鼠标所在窗口
        /// </summary>
        Enabled = 1,

        /// <summary>
        /// 禁用：始终使用前台窗口
        /// </summary>
        Disabled = 2
    }
}
