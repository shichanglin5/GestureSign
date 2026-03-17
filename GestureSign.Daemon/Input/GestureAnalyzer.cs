using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.PointPatterns;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.Daemon.Input
{
    internal static class GestureAnalyzer
    {
        private const double TapStrictMovementPx = 18;
        private const double TapRelaxedMajorityMovementPx = 20;
        private const int TapExtraAllowancePerFingerMs = 30;

        public static GestureAnalysis Analyze(List<List<Point>> allPoints, int fingerCount, double durationMs)
        {
            var analysis = new GestureAnalysis
            {
                FingerCount = fingerCount,
                TrajectoryCount = allPoints?.Count ?? 0,
                DurationMs = durationMs,
            };

            if (allPoints == null || allPoints.Count == 0)
                return analysis;

            var distances = new List<double>(allPoints.Count);
            var directionAngles = new List<double>(allPoints.Count);

            foreach (var stroke in allPoints)
            {
                if (stroke == null || stroke.Count < 2)
                {
                    distances.Add(0);
                    continue;
                }

                // 用首点最大偏离距离而非累计路径长度，
                // 避免手指原地抖动被累加放大导致 Tap 误判为 Trajectory
                var origin = stroke[0];
                double maxDeviation = 0;
                for (int index = 1; index < stroke.Count; index++)
                {
                    double d = PointPatternMath.GetDistance(origin, stroke[index]);
                    if (d > maxDeviation)
                        maxDeviation = d;
                }

                distances.Add(maxDeviation);

                var first = stroke[0];
                var last = stroke[stroke.Count - 1];
                directionAngles.Add(Math.Atan2(last.Y - first.Y, last.X - first.X));
            }

            analysis.MaxPerFingerDistance = distances.Count > 0 ? distances.Max() : 0;
            analysis.AveragePerFingerDistance = distances.Count > 0 ? distances.Average() : 0;
            analysis.StationaryFingerCount = distances.Count(d => d < TipTapRecognition.DefaultFixStillThresholdPx);

            if (directionAngles.Count > 1)
            {
                double averageAngle = directionAngles.Average();
                analysis.DirectionVariance = directionAngles.Average(angle => Math.Abs(NormalizeAngle(angle - averageAngle)));
            }

            analysis.IsTapLike = IsTapLike(distances, fingerCount, durationMs, analysis.MaxPerFingerDistance, analysis.AveragePerFingerDistance);

            return analysis;
        }

        /// <summary>
        /// 每多一指（相比 2 指）增加的位移容错像素数。
        /// </summary>
        private const double TapExtraAllowancePerFingerPx = 5;

        private static bool IsTapLike(
            IReadOnlyList<double> distances,
            int fingerCount,
            double durationMs,
            double maxPerFingerDistance,
            double averagePerFingerDistance)
        {
            // 多指 tap 时手指不可能完全同时抬起，依次抬起的时间差可达 20-30ms，
            // 因此对 3+ 指增加时间裕量，避免因手指间时间差导致误判为 Trajectory
            int extraAllowanceMs = fingerCount > 2 ? (fingerCount - 2) * TapExtraAllowancePerFingerMs : 0;
            if (durationMs > AppConfig.TapMaxDurationMs + extraAllowanceMs)
                return false;

            // 每多一指（相比 2 指）增加位移容错
            double extraPx = fingerCount > 2 ? (fingerCount - 2) * TapExtraAllowancePerFingerPx : 0;
            double strictThreshold = TapStrictMovementPx + extraPx;
            double relaxedThreshold = TapRelaxedMajorityMovementPx + extraPx;
            double distanceThreshold = AppConfig.TapDistanceThreshold + extraPx;

            if (maxPerFingerDistance < strictThreshold)
                return true;

            // 3+ 指允许个别手指有较大抖动，只要多数手指静止且均值小
            if (fingerCount < 3 || distances == null || distances.Count < fingerCount)
                return false;

            int withinRelaxedThresholdCount = distances.Count(distance => distance <= relaxedThreshold);
            if (withinRelaxedThresholdCount < fingerCount - 1)
                return false;

            return averagePerFingerDistance < strictThreshold
                && maxPerFingerDistance <= distanceThreshold;
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI) angle -= Math.PI * 2;
            while (angle < -Math.PI) angle += Math.PI * 2;
            return angle;
        }
    }
}
