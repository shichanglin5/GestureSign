using System;

namespace GestureSign.Common.Input
{
    [Serializable]
    public class GestureAnalysis
    {
        public int FingerCount { get; set; }
        public int TrajectoryCount { get; set; }
        public double DurationMs { get; set; }
        public int StationaryFingerCount { get; set; }
        public double AveragePerFingerDistance { get; set; }
        public double MaxPerFingerDistance { get; set; }
        public double DirectionVariance { get; set; }
        public double DistanceChangeRatio { get; set; }
        public bool IsTapLike { get; set; }
    }
}
