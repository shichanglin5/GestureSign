using GestureSign.PointPatterns;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.Common.Gestures
{
    /// <summary>
    /// 单点 stroke 的显示样式（仅用于 UI 渲染，不序列化）。
    /// </summary>
    public enum StrokeDisplayStyle
    {
        /// <summary>实心圆点（默认，Tap 手指 / TipTap fix 手指）</summary>
        FilledDot = 0,
        /// <summary>空心大圆（TipTap 的 tap 手指）</summary>
        HollowCircle = 1,
        /// <summary>实心圆点 + 外圈（Click 手指）</summary>
        RingedDot = 2,
    }

    [Serializable]
    public class PointPattern : IPointPattern
    {
        public PointPattern(Point[][] points, int fingerCount = 0)
        {
            Points = points;
            FingerCount = fingerCount;
        }

        public PointPattern(IEnumerable<List<Point>> points, int fingerCount = 0)
        {
            Points = points.Select(l => l.ToArray()).ToArray();
            FingerCount = fingerCount;
        }

        public Point[][] Points { get; set; }
        public int FingerCount { get; set; }

        /// <summary>
        /// 每个 stroke 的显示样式。仅用于 UI 渲染，长度应与 Points.Length 匹配。
        /// 为 null 时全部按 FilledDot 渲染。不序列化。
        /// </summary>
        [JsonIgnore]
        [NonSerialized]
        private StrokeDisplayStyle[] _strokeStyles;

        [JsonIgnore]
        public StrokeDisplayStyle[] StrokeStyles
        {
            get => _strokeStyles;
            set => _strokeStyles = value;
        }
    }
}
