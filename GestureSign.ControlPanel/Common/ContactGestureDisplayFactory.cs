using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.ControlPanel.Common
{
    public static class ContactGestureDisplayFactory
    {
        public static IGesture CreateDisplayGesture(RecordedGestureDefinitionResult definition)
        {
            if (definition == null)
                return null;

            switch (definition.Type)
            {
                case GestureSign.Common.Input.RecordedGestureType.Tap:
                    return CreateTapDisplay(definition);
                case GestureSign.Common.Input.RecordedGestureType.TipTap:
                    return CreateTipTapDisplay(definition);
                default:
                    return definition.TrajectoryGesture;
            }
        }

        private static IGesture CreateTapDisplay(RecordedGestureDefinitionResult definition)
        {
            int fingerCount = definition.FingerCount <= 0 ? 2 : definition.FingerCount;
            var strokes = new List<List<Point>>();
            for (int i = 0; i < fingerCount; i++)
            {
                strokes.Add(new List<Point>
                {
                    new Point(20 + i * 24, 30)
                });
            }

            var pattern = new PointPattern(strokes, fingerCount);

            return new Gesture(definition.Name, new[] { pattern }, fingerCount)
            {
                Id = definition.GestureId,
                Modifiers = definition.TapGesture?.Modifiers ?? GestureModifiers.Default,
            };
        }

        /// <summary>
        /// 确保 RecordedGestureDefinitionResult 中的 Id 和 Name 已赋值。
        /// 如果为空则自动生成，并同步到 definition.GestureId/Name。
        /// </summary>
        public static void EnsureIdentity(RecordedGestureDefinitionResult definition)
        {
            if (definition == null) return;

            switch (definition.Type)
            {
                case RecordedGestureType.Tap when definition.TapGesture != null:
                    definition.TapGesture.Id ??= Guid.NewGuid().ToString("N");
                    definition.TapGesture.Name ??= ContactGestureText.GetTapName(definition.TapGesture.FingerCount);
                    definition.GestureId = definition.TapGesture.Id;
                    definition.Name = definition.TapGesture.Name;
                    break;
                case RecordedGestureType.TipTap when definition.TipTapGesture != null:
                    definition.TipTapGesture.Id ??= Guid.NewGuid().ToString("N");
                    definition.TipTapGesture.Name ??= ContactGestureText.GetTipTapName(definition.TipTapGesture.FingerCount);
                    definition.GestureId = definition.TipTapGesture.Id;
                    definition.Name = definition.TipTapGesture.Name;
                    break;
            }
        }

        /// <summary>
        /// 将 contact gesture definition 保存到 GlobalApp 的 ContactGestures 中。
        /// 自动调用 EnsureIdentity 确保 Id/Name 已赋值。
        /// </summary>
        public static bool SaveToGlobalApp(RecordedGestureDefinitionResult definition)
        {
            if (definition == null) return false;

            var global = ApplicationManager.Instance.GetGlobalApplication();
            global.ContactGestures ??= new ContactGestureSettings();

            EnsureIdentity(definition);

            switch (definition.Type)
            {
                case RecordedGestureType.Tap when definition.TapGesture != null:
                    global.ContactGestures.Taps.RemoveAll(t => t.Id == definition.TapGesture.Id);
                    global.ContactGestures.Taps.Add(definition.TapGesture);
                    break;
                case RecordedGestureType.TipTap when definition.TipTapGesture != null:
                    global.ContactGestures.TipTaps.RemoveAll(t => t.Id == definition.TipTapGesture.Id);
                    global.ContactGestures.TipTaps.Add(definition.TipTapGesture);
                    break;
                default:
                    return false;
            }

            return true;
        }

        private static IGesture CreateTipTapDisplay(RecordedGestureDefinitionResult definition)
        {
            var config = definition.TipTapGesture;
            int total = config?.FingerCount ?? definition.FingerCount;
            int fix = config?.FixFingerCount ?? 1;

            // 先收集带样式标记的 (point, style) 对
            var entries = new List<(Point Point, StrokeDisplayStyle Style)>();

            for (int index = 0; index < fix; index++)
            {
                entries.Add((new Point(45 + index * 18, 32), StrokeDisplayStyle.FilledDot));
            }

            int tapX = config?.Direction switch
            {
                ContactGestureDirection.Left => 18,
                ContactGestureDirection.Right => 95,
                _ => 60,
            };

            entries.Add((new Point(tapX, 28), StrokeDisplayStyle.HollowCircle));

            // 按 X 排序以保持一致的视觉顺序
            entries.Sort((a, b) => a.Point.X.CompareTo(b.Point.X));

            var strokes = entries.Select(e => new List<Point> { e.Point }).ToList();
            var styles = entries.Select(e => e.Style).ToArray();

            var pattern = new PointPattern(strokes, total)
            {
                StrokeStyles = styles,
            };
            return new Gesture(definition.Name, new[] { pattern }, total)
            {
                Id = definition.GestureId,
                Modifiers = definition.TipTapGesture?.Modifiers ?? GestureModifiers.Default,
            };
        }
    }
}
