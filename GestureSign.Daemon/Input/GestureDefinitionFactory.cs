using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using System.Linq;

namespace GestureSign.Daemon.Input
{
    internal static class GestureDefinitionFactory
    {
        public static RecordedGestureDefinitionResult Create(RecordedGestureSample sample, IGesture trajectoryGesture = null)
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
                    var click = GestureClassifier.CreateClickDefinition(sample);
                    var existingClick = ApplicationManager.Instance.GetGlobalClickDefinitions(click.FingerCount).FirstOrDefault();
                    if (existingClick != null)
                    {
                        click = CreateTransportClickDefinition(existingClick);
                        result.MatchedExistingDefinition = true;
                    }
                    result.GestureId = click.Id;
                    result.Name = click.Name;
                    result.ClickGesture = click;
                    break;
                case RecordedGestureType.Tap:
                    var tap = GestureClassifier.CreateTapDefinition(sample);
                    var existingTap = ApplicationManager.Instance.GetGlobalTapDefinitions(tap.FingerCount).FirstOrDefault();
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
                    var existingTipTap = ApplicationManager.Instance.GetGlobalTipTapDefinitions(tipTap.FingerCount)
                        .FirstOrDefault(t => t.FixFingerCount == tipTap.FixFingerCount && t.Direction == tipTap.Direction);
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

        private static ClickGestureConfig CreateTransportClickDefinition(ClickGestureConfig source)
        {
            if (source == null)
                return null;

            return new ClickGestureConfig
            {
                Id = source.Id,
                Name = source.Name,
                IsEnabled = source.IsEnabled,
                FingerCount = source.FingerCount,
                Recognition = source.Recognition == null
                    ? new ClickGestureRecognition()
                    : new ClickGestureRecognition
                    {
                        MaxPressDurationMs = source.Recognition.MaxPressDurationMs,
                        MaxMovementPx = source.Recognition.MaxMovementPx,
                        MinFingerCount = source.Recognition.MinFingerCount,
                    },
            };
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


