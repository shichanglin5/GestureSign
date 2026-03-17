using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.Localization;

namespace GestureSign.Common.Input
{
    public static class ContactGestureText
    {
        public static string GetClickName(int fingerCount)
        {
            return Format("ContactGestures.ClickNameFormat", "{0}-finger click", fingerCount);
        }

        public static string GetTapName(int fingerCount)
        {
            return Format("ContactGestures.TapNameFormat", "{0}-finger tap", fingerCount);
        }

        public static string GetTipTapName(int fingerCount)
        {
            return Format("ContactGestures.TipTapNameFormat", "TipTap {0}-finger", fingerCount);
        }

        public static string GetTipTapDetailedName(ContactGestureDirection direction, int fixFingerCount)
        {
            return Format("ContactGestures.TipTapDetailedNameFormat", "TipTap {0} ({1} fixed)", GetDirectionLabel(direction), fixFingerCount);
        }

        public static string GetRecordedGestureTypeText(RecordedGestureDefinitionResult definition)
        {
            int fingerCount = definition?.FingerCount ?? 0;
            switch (definition?.Type)
            {
                case RecordedGestureType.Click:
                    return Format("ContactGestures.ClickTypeFormat", "{0} finger click", fingerCount);
                case RecordedGestureType.Tap:
                    return Format("ContactGestures.TapTypeFormat", "{0} finger tap", fingerCount);
                case RecordedGestureType.TipTap:
                    return !string.IsNullOrWhiteSpace(definition.Name)
                        ? definition.Name
                        : GetTipTapName(fingerCount);
                case RecordedGestureType.Trajectory:
                    return Format("ContactGestures.TrajectoryTypeFormat", "Trajectory gesture, {0} fingers", fingerCount);
                default:
                    return GetText("ContactGestures.UnknownType", "Unknown");
            }
        }

        public static string GetDirectionLabel(ContactGestureDirection direction)
        {
            return direction switch
            {
                ContactGestureDirection.Left => GetText("ContactGestures.DirectionLeft", "Left"),
                ContactGestureDirection.Right => GetText("ContactGestures.DirectionRight", "Right"),
                ContactGestureDirection.Middle => GetText("ContactGestures.DirectionMiddle", "Middle"),
                _ => GetText("ContactGestures.DirectionAny", "Any"),
            };
        }

        private static string Format(string key, string fallback, params object[] args)
        {
            return string.Format(GetText(key, fallback), args);
        }

        private static string GetText(string key, string fallback)
        {
            var value = LocalizationProvider.Instance.GetTextValue(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}