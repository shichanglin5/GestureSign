namespace GestureSign.Common.Input
{
    /// <summary>
    /// 窗口目标模式 - 决定手势动作发送给哪个窗口
    /// </summary>
    public enum WindowTargetMode
    {
        /// <summary>
        /// 鼠标位置所在窗口 (动态)
        /// </summary>
        MousePosition = 0,

        /// <summary>
        /// 当前激活的前台窗口 (动态)
        /// </summary>
        ActiveWindow = 1,

        /// <summary>
        /// 手势起始位置所在窗口 (静态,捕获时确定)
        /// </summary>
        GestureStartPosition = 2
    }
}
