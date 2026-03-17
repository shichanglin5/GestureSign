namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 惯性滚动参数（存储在 ContinuousGestureConfig 中，由 InertialScrollExecutor 使用）
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
        /// 方向噪声比例 (0.10-0.50)。
        /// 窗口内次轴/主轴位移比低于此值时，认为次轴为噪声并抑制。
        /// 值越小锁轴越激进，值越大越容易双轴放开。
        /// </summary>
        public double NoiseRatio { get; set; } = 0.25;

        /// <summary>
        /// 锁轴状态退回双轴前，次轴位移至少要达到的绝对距离。
        /// 用于过滤明显单轴滚动中的轻微横向/纵向抖动。
        /// </summary>
        public double LockExitMinorDistancePx { get; set; } = 6.0;

        /// <summary>
        /// 已进入双轴状态后，重新锁回主轴时使用的比例倍率。
        /// 值越小越容易重新锁轴。
        /// </summary>
        public double RelockRatioMultiplier { get; set; } = 0.8;

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

        /// <summary>
        /// 触发惯性的最大末帧静止时长 (ms)。
        /// 手指抬起前静止超过此时长则不触发惯性，认为用户有意停止。
        /// </summary>
        public int MomentumTriggerMaxIdleMs { get; set; } = 120;

        /// <summary>
        /// 手指落下时判断为"静止"的速度阈值 (px/s)。
        /// 低于此阈值认为手指静止落下，惯性立即停止；高于此阈值且同向则继承惯性速度。
        /// </summary>
        public double MomentumStopThreshold { get; set; } = 500.0;
    }

    public enum ScrollDirection
    {
        Vertical,
        Horizontal,
        Both
    }
}
