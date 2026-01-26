using System;

namespace GestureSign.Common.Applications
{
    /// <summary>
    /// 表示二维速度向量,用于手势滑动速度计算
    /// </summary>
    public struct VelocityVector
    {
        /// <summary>
        /// 水平方向速度 (像素/秒)
        /// </summary>
        public double VelocityX { get; set; }

        /// <summary>
        /// 垂直方向速度 (像素/秒)
        /// </summary>
        public double VelocityY { get; set; }

        /// <summary>
        /// 速度大小 √(Vx² + Vy²) (像素/秒)
        /// </summary>
        public double Magnitude { get; set; }

        /// <summary>
        /// 速度计算时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 创建速度向量
        /// </summary>
        /// <param name="vx">水平速度 (像素/秒)</param>
        /// <param name="vy">垂直速度 (像素/秒)</param>
        public VelocityVector(double vx, double vy)
        {
            VelocityX = vx;
            VelocityY = vy;
            Magnitude = Math.Sqrt(vx * vx + vy * vy);
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// 判断速度是否显著(超过阈值)
        /// </summary>
        /// <param name="threshold">速度阈值 (像素/秒),默认 50</param>
        /// <returns>速度大小是否超过阈值</returns>
        public bool IsSignificant(double threshold = 50)
        {
            return Magnitude >= threshold;
        }
    }
}
