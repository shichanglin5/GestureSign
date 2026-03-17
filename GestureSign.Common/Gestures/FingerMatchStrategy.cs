namespace GestureSign.Common.Gestures
{
    public enum FingerMatchStrategy
    {
        /// <summary>
        /// 继承系统全局配置（默认值，JSON 缺失时为 0，向后兼容）
        /// </summary>
        Inherit = 0,

        /// <summary>
        /// 全部手指匹配 — 所有手指的轨迹都参与比较
        /// </summary>
        AllFingers = 1,

        /// <summary>
        /// 特征手指匹配 — 仅用特征手指的轨迹进行比较
        /// </summary>
        FeatureFinger = 2,
    }
}
