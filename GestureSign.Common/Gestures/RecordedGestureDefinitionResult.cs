using System;
using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;

namespace GestureSign.Common.Gestures
{
    [Serializable]
    public class RecordedGestureDefinitionResult
    {
        public RecordedGestureType Type { get; set; }
        public string GestureId { get; set; }
        public bool MatchedExistingDefinition { get; set; }
        public string Name { get; set; }
        public int FingerCount { get; set; }
        public string DiagnosticData { get; set; }
        public IGesture TrajectoryGesture { get; set; }
        public TapGestureConfig TapGesture { get; set; }
        public ClickGestureConfig ClickGesture { get; set; }
        public TipTapGestureConfig TipTapGesture { get; set; }
    }
}



