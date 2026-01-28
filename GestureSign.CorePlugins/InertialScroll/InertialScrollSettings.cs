using System;

namespace GestureSign.CorePlugins.InertialScroll
{
    /// <summary>
    /// 跟手滚动插件配置
    /// </summary>
    public class InertialScrollSettings
    {
        /// <summary>
        /// 滚动方向
        /// </summary>
        public ScrollDirection Direction { get; set; } = ScrollDirection.Vertical;

        /// <summary>
        /// 滚动细腻度 (0.5-100.0)
        /// 表示多少像素位移等于一个标准滚轮单位(WHEEL_DELTA=120)
        /// 值越大滚动越细腻/越慢，值越小滚动越粗糙/越快
        /// 默认 30.0: 30像素 = 120 wheel delta, 即 1像素 = 4 wheel delta
        /// </summary>
        public double PixelsPerScrollUnit { get; set; } = 30.0;

        /// <summary>
        /// 加速因子 (0.1-5.0), 控制快速滑动时的加速程度
        /// 值越大，快速滑动时滚动速度提升越明显
        /// 默认 1.0: 中速时达到 1:1 映射，快速时适度加速
        /// </summary>
        public double AccelerationFactor { get; set; } = 1.0;

        /// <summary>
        /// 是否反向垂直滚动
        /// </summary>
        public bool ReverseDirection { get; set; } = false;

        /// <summary>
        /// 是否反向水平滚动
        /// </summary>
        public bool ReverseHorizontalDirection { get; set; } = false;

        /// <summary>
        /// 次轴阈值 - 防止滚动抖动 (0.0-0.8)
        /// 当次方向位移占比小于此值时，忽略次方向滚动
        /// 例如: 0.3 表示次方向位移小于主方向的30%时忽略
        /// 默认 0.3 (30%)
        /// </summary>
        public double MinorAxisThreshold { get; set; } = 0.3;

        /// <summary>
        /// 是否启用 WinUI/UWP 应用检测和自动滚动倍率调整
        /// </summary>
        public bool EnableWinUIDetection { get; set; } = true;

        /// <summary>
        /// WinUI/UWP 应用滚动倍数 (1.0-10.0)
        /// WinUI/UWP 应用对滚轮事件的响应比 Win32 应用慢，需要额外倍数补偿
        /// 默认 3.0
        /// </summary>
        public double WinUIScrollMultiplier { get; set; } = 3.0;
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
