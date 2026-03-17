using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;

namespace GestureSign.PointPatterns
{
    public class PointPatternAnalyzer
    {
        #region Constructors

        public PointPatternAnalyzer()
        {
            // Default precision to 100 (number or interpolation points)
            Precision = 100;
            // Default tap threshold: 2x default MinimumPointDistance (20px)
            TapThreshold = 40;
        }

        public PointPatternAnalyzer(IEnumerable<PointsPatternSet> PointPatternSet)
            : this()
        {
            // Instantiate PointPatternAnalyzer class with a PointPatternSet
            this.PointPatternSet = PointPatternSet;
        }

        public PointPatternAnalyzer(IEnumerable<PointsPatternSet> PointPatternSet, int Precision)
            : this()
        {
            // Instantiate PointPatternAnalyzer class with a PointPatternSet and Precision
            this.PointPatternSet = PointPatternSet;
            this.Precision = Precision;
        }

        #endregion

        #region Public Properties

        public int Precision { get; set; }
        public IEnumerable<PointsPatternSet> PointPatternSet { get; set; }//
        public int TapThreshold { get; set; } // Distance threshold for distinguishing tap from swipe gestures

        #endregion

        #region Public Methods

        public PointPatternMatchResult[] GetPointPatternMatchResults(Point[] Points)
        {
            // Create a list of PointPatternMatchResults to hold final results and group results of point pattern set comparison
            List<PointPatternMatchResult> comparisonResults = new List<PointPatternMatchResult>(PointPatternSet.Count());
            var targetPattern = new PointsPatternSet(null, Points);

            // Enumerate each point patterns
            foreach (var pointPatternSet in PointPatternSet)
            {
                // Calculate probability of each point pattern
                comparisonResults.Add(GetPointPatternMatchBreakdown(pointPatternSet, targetPattern).ToMatchResult());
            }

            // Return comparison results ordered by highest probability
            return comparisonResults.ToArray();//.OrderByDescending(ppmr => ppmr.Probability).
        }

        public PointPatternMatchResult GetPointPatternMatchResult(PointsPatternSet compareTo, PointsPatternSet points)
        {
            return GetPointPatternMatchBreakdown(compareTo, points).ToMatchResult();
        }

        public PointPatternScoreBreakdown GetPointPatternMatchBreakdown(PointsPatternSet compareTo, PointsPatternSet points)
        {
            PointPatternScoreBreakdown comparisonResults = new PointPatternScoreBreakdown();

            // Check if either pattern is a tap/click gesture (very small movement)
            bool compareToIsTap = IsTapGesture(compareTo.Points);
            bool pointsIsTap = IsTapGesture(points.Points);

            comparisonResults.Name = compareTo.Name;
            comparisonResults.CompareToIsTap = compareToIsTap;
            comparisonResults.PointsIsTap = pointsIsTap;

            if (compareToIsTap || pointsIsTap)
            {
                // Tap gesture matching: both must be taps
                if (compareToIsTap && pointsIsTap)
                {
                    comparisonResults.Probability = 100d;
                    comparisonResults.AngularProbability = 100d;
                    comparisonResults.IsTapMatch = true;
                    // System.Diagnostics.Debug.WriteLine($"[PointPatternAnalyzer] Both are tap gestures → 100%");
                }
                else
                {
                    comparisonResults.Probability = 0d;
                    comparisonResults.AngularProbability = 0d;
                    comparisonResults.IsTapMatch = false;
                    // System.Diagnostics.Debug.WriteLine($"[PointPatternAnalyzer] Tap/swipe mismatch: saved={compareToIsTap}, input={pointsIsTap} → 0%");
                }
            }
            else
            {
                double[] aDeltas = new double[Precision];
                double[] aCompareToAngles = compareTo.GetAngularMargins(Precision);
                double[] aCompareAngles = points.GetAngularMargins(Precision);

                for (int i = 0; i <= aCompareToAngles.Length - 1; i++)
                    aDeltas[i] = PointPatternMath.GetAngularDelta(aCompareToAngles[i], aCompareAngles[i]);

                // Create new PointPatternMatchResult object to hold results from comparison
                double averageAngularDelta = aDeltas.Average();
                double angularProbability = PointPatternMath.GetProbabilityFromAngularDelta(averageAngularDelta);
                var structuralPenalty = GetStructuralPenaltyBreakdown(aCompareToAngles, aCompareAngles);
                comparisonResults.AngularProbability = angularProbability;
                comparisonResults.AverageAngularDeltaDegrees = PointPatternMath.GetDegreeFromRadian(averageAngularDelta);
                comparisonResults.StructuralPenalty = structuralPenalty.TotalPenalty;
                comparisonResults.SignedTurnPenalty = structuralPenalty.SignedTurnPenalty;
                comparisonResults.AbsoluteTurnPenalty = structuralPenalty.AbsoluteTurnPenalty;
                comparisonResults.SignedTurnDeltaDegrees = structuralPenalty.SignedTurnDeltaDegrees;
                comparisonResults.AbsoluteTurnDeltaDegrees = structuralPenalty.AbsoluteTurnDeltaDegrees;
                comparisonResults.Probability = Math.Max(0d, angularProbability - structuralPenalty.TotalPenalty);
            }
            // Return results of the comparison
            return comparisonResults;
        }

        private static StructuralPenaltyBreakdown GetStructuralPenaltyBreakdown(double[] compareToAngles, double[] compareAngles)
        {
            if (compareToAngles == null || compareAngles == null || compareToAngles.Length < 2 || compareAngles.Length < 2)
                return new StructuralPenaltyBreakdown(0d, 0d, 0d, 0d, 0d);

            double signedTurnDelta = Math.Abs(GetSignedTurnSum(compareToAngles) - GetSignedTurnSum(compareAngles));
            double absoluteTurnDelta = Math.Abs(GetAbsoluteTurnSum(compareToAngles) - GetAbsoluteTurnSum(compareAngles));

            double signedTurnDeltaDegrees = PointPatternMath.GetDegreeFromRadian(signedTurnDelta);
            double absoluteTurnDeltaDegrees = PointPatternMath.GetDegreeFromRadian(absoluteTurnDelta);
            double signedTurnPenalty = Math.Min(5d, signedTurnDeltaDegrees / 36d);
            double absoluteTurnPenalty = Math.Min(5d, absoluteTurnDeltaDegrees / 54d);

            return new StructuralPenaltyBreakdown(
                signedTurnPenalty + absoluteTurnPenalty,
                signedTurnPenalty,
                absoluteTurnPenalty,
                signedTurnDeltaDegrees,
                absoluteTurnDeltaDegrees);
        }

        private static double GetSignedTurnSum(double[] angles)
        {
            double total = 0d;
            for (int index = 1; index < angles.Length; index++)
            {
                total += NormalizeSignedAngle(angles[index] - angles[index - 1]);
            }

            return total;
        }

        private static double GetAbsoluteTurnSum(double[] angles)
        {
            double total = 0d;
            for (int index = 1; index < angles.Length; index++)
            {
                total += Math.Abs(NormalizeSignedAngle(angles[index] - angles[index - 1]));
            }

            return total;
        }

        private static double NormalizeSignedAngle(double angle)
        {
            while (angle > Math.PI)
                angle -= Math.PI * 2;

            while (angle < -Math.PI)
                angle += Math.PI * 2;

            return angle;
        }

        private bool IsTapGesture(Point[] points)
        {
            if (points == null || points.Length == 0)
                return false;

            // Single point is always a tap
            if (points.Length == 1)
                return true;

            // Calculate max displacement from the start point
            // Using displacement instead of cumulative distance avoids false negatives
            // from finger jitter (e.g., 0 → 1 → -1 → 0 has displacement 1, not cumulative 2)
            Point start = points[0];
            double maxDisplacement = 0;
            for (int i = 1; i < points.Length; i++)
            {
                double displacement = PointPatternMath.GetDistance(start, points[i]);
                if (displacement > maxDisplacement)
                    maxDisplacement = displacement;
            }

            bool isTap = maxDisplacement < TapThreshold;
            return isTap;
        }

        private readonly struct StructuralPenaltyBreakdown
        {
            public StructuralPenaltyBreakdown(double totalPenalty, double signedTurnPenalty, double absoluteTurnPenalty, double signedTurnDeltaDegrees, double absoluteTurnDeltaDegrees)
            {
                TotalPenalty = totalPenalty;
                SignedTurnPenalty = signedTurnPenalty;
                AbsoluteTurnPenalty = absoluteTurnPenalty;
                SignedTurnDeltaDegrees = signedTurnDeltaDegrees;
                AbsoluteTurnDeltaDegrees = absoluteTurnDeltaDegrees;
            }

            public double TotalPenalty { get; }

            public double SignedTurnPenalty { get; }

            public double AbsoluteTurnPenalty { get; }

            public double SignedTurnDeltaDegrees { get; }

            public double AbsoluteTurnDeltaDegrees { get; }
        }

        #endregion
    }
}
