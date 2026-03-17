using System;

namespace GestureSign.Common.Gestures
{
    [Serializable]
    public class Gesture : IGesture
    {
        #region Constructors
        public Gesture()
        { }
        public Gesture(string name, PointPattern[] pointPatterns, int fingerCount = 0)
        {
            this.Name = name;
            this.PointPatterns = pointPatterns;
            // If fingerCount is not provided, try to get it from the first PointPattern
            this.FingerCount = fingerCount > 0 ? fingerCount : (pointPatterns?.Length > 0 ? pointPatterns[0].FingerCount : 0);
        }

        #endregion

        #region IPointPattern Instance Properties

        public string Id { get; set; }

        public string Name { get; set; }

        public PointPattern[] PointPatterns { get; set; }

        public int FingerCount { get; set; }

        public FingerMatchStrategy MatchStrategy { get; set; } = FingerMatchStrategy.Inherit;

        public GestureModifiers Modifiers { get; set; } = GestureModifiers.Default;

        #endregion
    }
}
