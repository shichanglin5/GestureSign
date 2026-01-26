using System;

namespace GestureSign.CorePlugins.InertialScroll
{
    /// <summary>
    /// 惯性滚动插件配置
    /// </summary>
    public class InertialScrollSettings
    {
        /// <summary>
        /// 滚动方向
        /// </summary>
        public ScrollDirection Direction { get; set; } = ScrollDirection.Vertical;

        /// <summary>
        /// 是否启用惯性效果
        /// </summary>
        public bool EnableInertia { get; set; } = true;

        /// <summary>
        /// 惯性强度 (0.5-2.0),影响初始滚动速度
        /// </summary>
        public double InertiaStrength { get; set; } = 1.0;

        /// <summary>
        /// 惯性持续时间(秒,0.5-3.0),防止无限滚动
        /// </summary>
        public double InertiaDuration { get; set; } = 1.5;

        /// <summary>
        /// 速度衰减率 (0.85-0.98),每帧速度保留比例
        /// </summary>
        public double DecayRate { get; set; } = 0.95;

        /// <summary>
        /// 距离倍数 (0.5-3.0),调整滚动灵敏度
        /// </summary>
        public double DistanceMultiplier { get; set; } = 0.3;

        /// <summary>
        /// 最小速度阈值(像素/秒),低于此值停止惯性
        /// </summary>
        public double MinimumVelocity { get; set; } = 50;

        /// <summary>
        /// 像素到滚动单位的转换比例 (默认 5 像素 = 1 滚动单位)
        /// 范围: 3.0 - 30.0, 值越小滚动越细腻
        /// </summary>
        public double PixelsPerScrollUnit { get; set; } = 5.0;

        /// <summary>
        /// 是否反向滚动
        /// </summary>
        public bool ReverseDirection { get; set; } = false;
    }

    /// <summary>
    /// 滚动方向枚举
    /// </summary>
    public enum ScrollDirection
    {
        /// <summary>
        /// 垂直滚动
        /// </summary>
        Vertical,

        /// <summary>
        /// 水平滚动
        /// </summary>
        Horizontal,

        /// <summary>
        /// 双向滚动(根据手势方向自动选择)
        /// </summary>
        Both
    }
}
