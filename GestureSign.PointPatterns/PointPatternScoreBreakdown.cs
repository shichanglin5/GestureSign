using System;

namespace GestureSign.PointPatterns
{
    public sealed class PointPatternScoreBreakdown
    {
        public string Name { get; set; }

        public double Probability { get; set; }

        public double AngularProbability { get; set; }

        public double AverageAngularDeltaDegrees { get; set; }

        public double StructuralPenalty { get; set; }

        public double SignedTurnPenalty { get; set; }

        public double AbsoluteTurnPenalty { get; set; }

        public double SignedTurnDeltaDegrees { get; set; }

        public double AbsoluteTurnDeltaDegrees { get; set; }

        public bool CompareToIsTap { get; set; }

        public bool PointsIsTap { get; set; }

        public bool IsTapMatch { get; set; }

        public PointPatternMatchResult ToMatchResult()
        {
            return new PointPatternMatchResult
            {
                Name = Name,
                Probability = Probability,
                AngularProbability = AngularProbability,
                StructuralPenalty = StructuralPenalty,
            };
        }
    }
}
