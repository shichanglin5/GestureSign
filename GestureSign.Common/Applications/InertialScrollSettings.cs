namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 惯性滚动参数（存储在 ContinuousGestureConfig 中）
    /// 字段与 CorePlugins.InertialScroll.InertialScrollSettings 一致
    /// </summary>
    public class InertialScrollSettings
    {
        /// <summary>
        /// 滚动方向: 0=Vertical, 1=Horizontal, 2=Both
        /// </summary>
        public ScrollDirection Direction { get; set; } = ScrollDirection.Both;

        /// <summary>
        /// 滚动细腻度 (0.5-100.0)
        /// 多少像素位移等于一个标准滚轮单位(WHEEL_DELTA=120)
        /// </summary>
        public double PixelsPerScrollUnit { get; set; } = 150.0;

        /// <summary>
        /// 加速因子 (0.1-5.0)
        /// </summary>
        public double AccelerationFactor { get; set; } = 1.5;

        /// <summary>
        /// 是否反向垂直滚动
        /// </summary>
        public bool ReverseDirection { get; set; }

        /// <summary>
        /// 是否反向水平滚动
        /// </summary>
        public bool ReverseHorizontalDirection { get; set; }

        /// <summary>
        /// 次轴阈值 (0.0-0.8)
        /// </summary>
        public double MinorAxisThreshold { get; set; } = 0.3;

        /// <summary>
        /// 是否启用 WinUI/UWP 应用检测
        /// </summary>
        public bool EnableWinUIDetection { get; set; } = true;

        /// <summary>
        /// WinUI/UWP 应用滚动倍数 (1.0-10.0)
        /// </summary>
        public double WinUIScrollMultiplier { get; set; } = 3.0;

        /// <summary>
        /// Enable momentum scrolling after finger release.
        /// </summary>
        public bool EnableMomentum { get; set; } = true;

        /// <summary>
        /// Exponential decay time constant for momentum (ms).
        /// </summary>
        public double MomentumTimeConstantMs { get; set; } = 600.0;

        /// <summary>
        /// Minimum velocity to continue momentum (px/s).
        /// </summary>
        public double MomentumMinVelocity { get; set; } = 10.0;

        /// <summary>
        /// Maximum momentum duration (ms).
        /// </summary>
        public double MomentumMaxDurationMs { get; set; } = 1500.0;

        /// <summary>
        /// Momentum timer tick interval (ms). Not intended for end-user tuning.
        /// </summary>
        public double MomentumTickMs { get; set; } = 8.0;
    }

    public enum ScrollDirection
    {
        Vertical,
        Horizontal,
        Both
    }
}
