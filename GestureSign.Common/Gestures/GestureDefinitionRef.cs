namespace GestureSign.Common.Gestures
{
    public enum GestureDefinitionKind
    {
        Trajectory = 0,
        Tap = 1,
        TipTap = 2,
        TwoFingerScroll = 3,
        TwoFingerZoom = 4,
    }

    public class GestureDefinitionRef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public GestureDefinitionKind Kind { get; set; }
        public int FingerCount { get; set; }
    }
}
