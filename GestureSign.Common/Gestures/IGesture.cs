namespace GestureSign.Common.Gestures
{
    public interface IGesture
    {
        string Id { get; set; }
        string Name { get; set; }

        PointPattern[] PointPatterns { get; set; }

        int FingerCount { get; set; }

        FingerMatchStrategy MatchStrategy { get; set; }
    }
}
