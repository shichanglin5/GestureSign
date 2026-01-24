using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
                comparisonResults.Add(GetPointPatternMatchResult(pointPatternSet, targetPattern));
            }

            // Return comparison results ordered by highest probability
            return comparisonResults.ToArray();//.OrderByDescending(ppmr => ppmr.Probability).
        }

        public PointPatternMatchResult GetPointPatternMatchResult(PointsPatternSet compareTo, PointsPatternSet points)
        {
            PointPatternMatchResult comparisonResults = new PointPatternMatchResult();

            // Check if either pattern is a tap/click gesture (very small movement)
            bool compareToIsTap = IsTapGesture(compareTo.Points);
            bool pointsIsTap = IsTapGesture(points.Points);

            if (compareToIsTap || pointsIsTap)
            {
                // Tap gesture matching: both must be taps
                if (compareToIsTap && pointsIsTap)
                {
                    comparisonResults.Probability = 100d;
                    // System.Diagnostics.Debug.WriteLine($"[PointPatternAnalyzer] Both are tap gestures → 100%");
                }
                else
                {
                    comparisonResults.Probability = 0d;
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
                comparisonResults.Probability = PointPatternMath.GetProbabilityFromAngularDelta(aDeltas.Average());
            }
            comparisonResults.Name = compareTo.Name;
            // Return results of the comparison
            return comparisonResults;
        }

        private bool IsTapGesture(Point[] points)
        {
            if (points == null || points.Length == 0)
                return false;

            // Single point is always a tap
            if (points.Length == 1)
                return true;

            // Calculate total path length
            double totalDistance = 0;
            for (int i = 1; i < points.Length; i++)
            {
                totalDistance += PointPatternMath.GetDistance(points[i - 1], points[i]);
            }

            // If total movement is less than TapThreshold, treat as tap
            bool isTap = totalDistance < TapThreshold;

            // System.Diagnostics.Debug.WriteLine($"[PointPatternAnalyzer] IsTapGesture: {points.Length} points, distance={totalDistance:F1}px, threshold={TapThreshold}px → {isTap}");
            return isTap;
        }

        #endregion
    }
}
