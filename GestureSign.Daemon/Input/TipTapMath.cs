using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.PointPatterns;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.Daemon.Input
{
    internal static class TipTapMath
    {
        public static bool HasMinimumFixHoldBeforeTap(
            GestureSessionSnapshot session,
            IReadOnlyList<int> fixIds,
            int tapId,
            int minFixHoldMs)
        {
            if (session?.ContactDownTimesMs == null || fixIds == null || fixIds.Count == 0)
                return false;

            if (!session.ContactDownTimesMs.TryGetValue(tapId, out double tapDownMs))
                return false;

            double latestFixDownMs = double.MinValue;
            foreach (int fixId in fixIds)
            {
                if (!session.ContactDownTimesMs.TryGetValue(fixId, out double fixDownMs))
                    return false;

                latestFixDownMs = Math.Max(latestFixDownMs, fixDownMs);
            }

            return tapDownMs - latestFixDownMs >= minFixHoldMs;
        }

        public static ContactGestureDirection DetermineDirection(
            IEnumerable<double> fixXs,
            double tapX,
            double deadzone)
        {
            var xAnchors = fixXs?.ToList();
            if (xAnchors == null || xAnchors.Count == 0)
                return ContactGestureDirection.None;

            double leftOverflow = (xAnchors.Min() - deadzone) - tapX;
            double rightOverflow = tapX - (xAnchors.Max() + deadzone);

            if (leftOverflow <= 0 && rightOverflow <= 0)
                return ContactGestureDirection.Middle;

            return rightOverflow > leftOverflow
                ? ContactGestureDirection.Right
                : ContactGestureDirection.Left;
        }

        /// <summary>
        /// 计算轨迹中任意点到首点的最大偏离距离。
        /// 用首点最大偏离而非累计路径长度，避免手指原地抖动（0→1→0）被累加放大。
        /// </summary>
        public static double GetTrajectoryDistance(IReadOnlyList<Point> trajectory)
        {
            if (trajectory == null || trajectory.Count < 2)
                return 0;

            var origin = trajectory[0];
            double maxDeviation = 0;
            for (int i = 1; i < trajectory.Count; i++)
            {
                double d = PointPatternMath.GetDistance(origin, trajectory[i]);
                if (d > maxDeviation)
                    maxDeviation = d;
            }
            return maxDeviation;
        }
    }
}