using System;
using System.Collections.Generic;
using System.Drawing;

namespace GestureSign.Common.Input
{
    public enum RecordedGestureType
    {
        Unknown = 0,
        Trajectory = 1,
        Tap = 2,
        TipTap = 3,
        Click = 4,
    }

    [Serializable]
    public class RecordedGestureSample
    {
        public int FingerCount { get; set; }
        public double DurationMs { get; set; }
        public GestureAnalysis Analysis { get; set; }
        public GestureSessionSnapshot Session { get; set; }
        public List<List<Point>> AllPoints { get; set; } = new List<List<Point>>();

        /// <summary>
        /// _pointsCaptured 的数据（原始屏幕像素坐标），用于诊断日志对比
        /// </summary>
        [NonSerialized]
        public Dictionary<int, List<Point>> FeatureTrajectories;
    }
}

