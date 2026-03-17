using GestureSign.Common.Gestures;
using System.Windows.Media;

namespace GestureSign.ControlPanel.Common
{
    public class GestureItem
    {
        public IGesture Gesture { get; set; }
        public string Applications { get; set; }
        public string Features { get; set; }
        public int PatternCount { get; set; }
        public DrawingImage GestureImage { get; set; }

        public int FingerCount => Gesture?.FingerCount ?? 0;

        public string FingerCountText => FingerCount > 0 ? $"{FingerCount}指" : "";

        /// <summary>
        /// 手势名称，用于列表显示（如"TipTap Right (1 fixed)"、"3指轻点"等）。
        /// </summary>
        public string GestureName => Gesture?.Name ?? "";
    }
}
