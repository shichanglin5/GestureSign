using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using System.Linq;
using System;

namespace GestureSign.Daemon.Input
{
    internal static class GestureDefinitionFactory
    {
        public static RecordedGestureDefinitionResult Create(RecordedGestureSample sample, IGesture trajectoryGesture = null, GestureModifiers modifiers = GestureModifiers.Default)
        {
            var type = GestureClassifier.Classify(sample);
            var result = new RecordedGestureDefinitionResult
            {
                Type = type,
                FingerCount = sample?.FingerCount ?? 0,
            };

            switch (type)
            {
                case RecordedGestureType.Click:
                    // Click 识别结果转为 Tap + PrimaryButtonDown，统一到 Tap 匹配路径
                    var tapFromClick = GestureClassifier.CreateTapDefinition(sample);
                    tapFromClick.Modifiers = modifiers;
                    var existingTapForClick = ApplicationManager.Instance.GetGlobalTapDefinitions(tapFromClick.FingerCount, modifiers).FirstOrDefault();
                    if (existingTapForClick != null)
                    {
                        tapFromClick = CreateTransportTapDefinition(existingTapForClick);
                        result.MatchedExistingDefinition = true;
                    }
                    result.Type = RecordedGestureType.Tap;
                    result.GestureId = tapFromClick.Id;
                    result.Name = tapFromClick.Name;
                    result.TapGesture = tapFromClick;
                    break;
                case RecordedGestureType.Tap:
                    var tap = GestureClassifier.CreateTapDefinition(sample);
                    tap.Modifiers = modifiers;
                    var existingTap = ApplicationManager.Instance.GetGlobalTapDefinitions(tap.FingerCount, modifiers).FirstOrDefault();
                    if (existingTap != null)
                    {
                        tap = CreateTransportTapDefinition(existingTap);
                        result.MatchedExistingDefinition = true;
                    }
                    result.GestureId = tap.Id;
                    result.Name = tap.Name;
                    result.TapGesture = tap;
                    break;
                case RecordedGestureType.TipTap:
                    var tipTap = GestureClassifier.CreateTipTapDefinition(sample);
                    tipTap.Modifiers = modifiers;
                    var existingTipTap = ApplicationManager.Instance.GetGlobalTipTapDefinitions(tipTap.FingerCount)
                        .FirstOrDefault(t => t.FixFingerCount == tipTap.FixFingerCount && t.Direction == tipTap.Direction && t.Modifiers == modifiers);
                    if (existingTipTap != null)
                    {
                        tipTap = CreateTransportTipTapDefinition(existingTipTap);
                        result.MatchedExistingDefinition = true;
                    }
                    result.GestureId = tipTap.Id;
                    result.Name = tipTap.Name;
                    result.TipTapGesture = tipTap;
                    break;
                default:
                    result.GestureId = trajectoryGesture?.Id;
                    result.Name = trajectoryGesture?.Name;
                    result.TrajectoryGesture = trajectoryGesture;
                    result.MatchedExistingDefinition = !string.IsNullOrEmpty(trajectoryGesture?.Id);
                    break;
            }

            return result;
        }

        public static RecordedGestureDefinitionResult CreateTipTapMatch(RecordedGestureSample sample, TipTapGestureConfig matchedConfig)
        {
            var result = new RecordedGestureDefinitionResult
            {
                Type = RecordedGestureType.TipTap,
                FingerCount = sample?.FingerCount ?? matchedConfig?.FingerCount ?? 0,
            };

            var tipTap = CreateTransportTipTapDefinition(matchedConfig)
                ?? GestureClassifier.CreateTipTapDefinition(sample);

            if (matchedConfig != null)
            {
                result.MatchedExistingDefinition = !string.IsNullOrEmpty(matchedConfig.Id);
            }

            result.GestureId = tipTap.Id;
            result.Name = tipTap.Name;
            result.TipTapGesture = tipTap;
            return result;
        }

        private static TapGestureConfig CreateTransportTapDefinition(TapGestureConfig source)
        {
            if (source == null)
                return null;

            return new TapGestureConfig
            {
                Id = source.Id,
                Name = source.Name,
                IsEnabled = source.IsEnabled,
                FingerCount = source.FingerCount,
                Modifiers = source.Modifiers,
                Recognition = source.Recognition == null
                    ? new TapGestureRecognition()
                    : new TapGestureRecognition
                    {
                        MaxDurationMs = source.Recognition.MaxDurationMs,
                        MaxMovementPx = source.Recognition.MaxMovementPx,
                        MinFingerCount = source.Recognition.MinFingerCount,
                    },
            };
        }

        private static TipTapGestureConfig CreateTransportTipTapDefinition(TipTapGestureConfig source)
        {
            if (source == null)
                return null;

            return new TipTapGestureConfig
            {
                Id = source.Id,
                Name = source.Name,
                IsEnabled = source.IsEnabled,
                FingerCount = source.FingerCount,
                FixFingerCount = source.FixFingerCount,
                Direction = source.Direction,
                Modifiers = source.Modifiers,
                Recognition = source.Recognition == null
                    ? new TipTapRecognition()
                    : new TipTapRecognition
                    {
                        FixMinHoldMs = source.Recognition.FixMinHoldMs,
                        MaxTapDurationMs = source.Recognition.MaxTapDurationMs,
                        FixStillThresholdPx = source.Recognition.FixStillThresholdPx,
                        TapMaxMovementPx = source.Recognition.TapMaxMovementPx,
                        DirectionDeadzonePx = source.Recognition.DirectionDeadzonePx,
                        RepeatCooldownMs = source.Recognition.RepeatCooldownMs,
                    },
            };
        }
    }
}


