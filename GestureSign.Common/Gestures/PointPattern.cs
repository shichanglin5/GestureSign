using GestureSign.PointPatterns;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.Common.Gestures
{
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
    }
}
