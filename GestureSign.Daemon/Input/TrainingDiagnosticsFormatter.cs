using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GestureSign.Daemon.Input
{
    internal static class TrainingDiagnosticsFormatter
    {
        public static string Format(
            RecordedGestureSample sample,
            RecordedGestureDefinitionResult definition,
            IReadOnlyList<TrainingDiagnosticFrame> frames,
            Devices sourceDevice)
        {
            var builder = new StringBuilder(8192);
            var invariant = CultureInfo.InvariantCulture;

            builder.AppendLine("GestureSign Recording Diagnostics");
            builder.AppendLine($"GeneratedAtUtc: {DateTime.UtcNow:O}");
            builder.AppendLine($"SourceDevice: {sourceDevice}");
            builder.AppendLine($"ResultType: {definition?.Type}");
            builder.AppendLine($"ResultName: {definition?.Name ?? string.Empty}");
            builder.AppendLine($"ResultGestureId: {definition?.GestureId ?? string.Empty}");
            builder.AppendLine($"MatchedExistingDefinition: {definition?.MatchedExistingDefinition ?? false}");
            builder.AppendLine($"FingerCount: {sample?.FingerCount ?? 0}");
            builder.AppendLine($"DurationMs: {FormatDouble(sample?.DurationMs ?? 0, invariant)}");
            builder.AppendLine();

            AppendAnalysis(builder, sample?.Analysis, invariant);
            AppendSession(builder, sample?.Session, invariant);
            AppendDefinition(builder, definition);
            AppendTrajectories(builder, sample?.Session, invariant);
            AppendFeatureTrajectories(builder, sample?.FeatureTrajectories);
            AppendFrames(builder, frames, invariant);

            return builder.ToString();
        }

        private static void AppendAnalysis(StringBuilder builder, GestureAnalysis analysis, CultureInfo culture)
        {
            builder.AppendLine("[Analysis]");
            if (analysis == null)
            {
                builder.AppendLine("null");
                builder.AppendLine();
                return;
            }

            builder.AppendLine($"DurationMs: {FormatDouble(analysis.DurationMs, culture)}");
            builder.AppendLine($"TrajectoryCount: {analysis.TrajectoryCount}");
            builder.AppendLine($"StationaryFingerCount: {analysis.StationaryFingerCount}");
            builder.AppendLine($"AveragePerFingerDistance: {FormatDouble(analysis.AveragePerFingerDistance, culture)}");
            builder.AppendLine($"MaxPerFingerDistance: {FormatDouble(analysis.MaxPerFingerDistance, culture)}");
            builder.AppendLine($"DirectionVariance: {FormatDouble(analysis.DirectionVariance, culture)}");
            builder.AppendLine($"DistanceChangeRatio: {FormatDouble(analysis.DistanceChangeRatio, culture)}");
            builder.AppendLine($"IsTapLike: {analysis.IsTapLike}");
            builder.AppendLine();
        }

        private static void AppendSession(StringBuilder builder, GestureSessionSnapshot session, CultureInfo culture)
        {
            builder.AppendLine("[Session]");
            if (session == null)
            {
                builder.AppendLine("null");
                builder.AppendLine();
                return;
            }

            builder.AppendLine($"FingerCount: {session.FingerCount}");
            builder.AppendLine($"DurationMs: {FormatDouble(session.DurationMs, culture)}");
            builder.AppendLine($"ActiveContactIds: {FormatSequence(session.ActiveContactIds)}");
            builder.AppendLine($"ContactDownOrder: {FormatSequence(session.ContactDownOrder)}");
            builder.AppendLine($"ContactUpOrder: {FormatSequence(session.ContactUpOrder)}");
            builder.AppendLine($"ContactDownTimesMs: {FormatDictionary(session.ContactDownTimesMs, culture)}");
            builder.AppendLine($"ContactUpTimesMs: {FormatDictionary(session.ContactUpTimesMs, culture)}");
            builder.AppendLine($"HasPrimaryButtonClick: {session.HasPrimaryButtonClick}");
            builder.AppendLine($"PrimaryButtonFingerCount: {session.PrimaryButtonFingerCount}");
            builder.AppendLine($"PrimaryButtonDownTimeMs: {FormatNullableDouble(session.PrimaryButtonDownTimeMs, culture)}");
            builder.AppendLine($"PrimaryButtonUpTimeMs: {FormatNullableDouble(session.PrimaryButtonUpTimeMs, culture)}");
            builder.AppendLine();
        }

        private static void AppendDefinition(StringBuilder builder, RecordedGestureDefinitionResult definition)
        {
            builder.AppendLine("[Definition]");
            if (definition == null)
            {
                builder.AppendLine("null");
                builder.AppendLine();
                return;
            }

            switch (definition.Type)
            {
                case RecordedGestureType.Click:
                    builder.AppendLine($"Click.FingerCount: {definition.ClickGesture?.FingerCount ?? 0}");
                    builder.AppendLine($"Click.MaxPressDurationMs: {definition.ClickGesture?.Recognition?.MaxPressDurationMs ?? 0}");
                    builder.AppendLine($"Click.MaxMovementPx: {definition.ClickGesture?.Recognition?.MaxMovementPx ?? 0}");
                    break;
                case RecordedGestureType.Tap:
                    builder.AppendLine($"Tap.FingerCount: {definition.TapGesture?.FingerCount ?? 0}");
                    builder.AppendLine($"Tap.MaxDurationMs: {definition.TapGesture?.Recognition?.MaxDurationMs ?? 0}");
                    builder.AppendLine($"Tap.MaxMovementPx: {definition.TapGesture?.Recognition?.MaxMovementPx ?? 0}");
                    break;
                case RecordedGestureType.TipTap:
                    builder.AppendLine($"TipTap.FingerCount: {definition.TipTapGesture?.FingerCount ?? 0}");
                    builder.AppendLine($"TipTap.FixFingerCount: {definition.TipTapGesture?.FixFingerCount ?? 0}");
                    builder.AppendLine($"TipTap.Direction: {definition.TipTapGesture?.Direction ?? ContactGestureDirection.None}");
                    builder.AppendLine($"TipTap.MaxTapDurationMs: {definition.TipTapGesture?.Recognition?.MaxTapDurationMs ?? 0}");
                    builder.AppendLine($"TipTap.FixStillThresholdPx: {definition.TipTapGesture?.Recognition?.FixStillThresholdPx ?? 0}");
                    builder.AppendLine($"TipTap.TapMaxMovementPx: {definition.TipTapGesture?.Recognition?.TapMaxMovementPx ?? 0}");
                    builder.AppendLine($"TipTap.DirectionDeadzonePx: {definition.TipTapGesture?.Recognition?.DirectionDeadzonePx ?? 0}");
                    break;
                case RecordedGestureType.Trajectory:
                    builder.AppendLine($"Trajectory.Name: {definition.TrajectoryGesture?.Name ?? string.Empty}");
                    builder.AppendLine($"Trajectory.Id: {definition.TrajectoryGesture?.Id ?? string.Empty}");
                    break;
            }

            builder.AppendLine();
        }

        private static void AppendTrajectories(StringBuilder builder, GestureSessionSnapshot session, CultureInfo culture)
        {
            builder.AppendLine("[Trajectories]");
            if (session?.ContactTrajectories == null || session.ContactTrajectories.Count == 0)
            {
                builder.AppendLine("none");
                builder.AppendLine();
                return;
            }

            foreach (var entry in session.ContactTrajectories.OrderBy(kvp => kvp.Key))
            {
                var points = entry.Value ?? new List<System.Drawing.Point>();
                builder.AppendLine($"Contact {entry.Key}: count={points.Count}; points={string.Join(" ", points.Select(p => $"({p.X},{p.Y})"))}");
            }

            builder.AppendLine();
        }

        private static void AppendFeatureTrajectories(StringBuilder builder, Dictionary<int, List<System.Drawing.Point>> featureTrajectories)
        {
            builder.AppendLine("[FeatureTrajectories]");
            if (featureTrajectories == null || featureTrajectories.Count == 0)
            {
                builder.AppendLine("none");
                builder.AppendLine();
                return;
            }

            foreach (var entry in featureTrajectories.OrderBy(kvp => kvp.Key))
            {
                var points = entry.Value ?? new List<System.Drawing.Point>();
                builder.AppendLine($"Contact {entry.Key}: count={points.Count}; points={string.Join(" ", points.Select(p => $"({p.X},{p.Y})"))}");
            }

            builder.AppendLine();
        }

        private static void AppendFrames(StringBuilder builder, IReadOnlyList<TrainingDiagnosticFrame> frames, CultureInfo culture)
        {
            builder.AppendLine("[RawFrames]");
            if (frames == null || frames.Count == 0)
            {
                builder.AppendLine("none");
                return;
            }

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                builder.Append($"{i:D3} {FormatDouble(frame.ElapsedMs, culture)}ms {frame.EventType} total={frame.TotalFingerCount} count={frame.Points.Count}");
                if (frame.Points.Count > 0)
                {
                    builder.Append(" :: ");
                    builder.Append(string.Join(" | ", frame.Points
                        .OrderBy(p => p.ContactId)
                        .Select(point => $"id={point.ContactId} state={point.State} raw=({point.RawX},{point.RawY}) translated=({point.TranslatedX},{point.TranslatedY})")));
                }

                builder.AppendLine();
            }
        }

        private static string FormatDictionary(IReadOnlyDictionary<int, double> values, CultureInfo culture)
        {
            if (values == null || values.Count == 0)
                return "[]";

            return "[" + string.Join(", ", values.OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key}:{FormatDouble(kvp.Value, culture)}")) + "]";
        }

        private static string FormatSequence(IEnumerable<int> values)
        {
            if (values == null)
                return "[]";

            return "[" + string.Join(", ", values) + "]";
        }

        private static string FormatDouble(double value, CultureInfo culture)
        {
            return value.ToString("0.###", culture);
        }

        private static string FormatNullableDouble(double? value, CultureInfo culture)
        {
            return value.HasValue ? FormatDouble(value.Value, culture) : string.Empty;
        }
    }

    internal sealed class TrainingDiagnosticFrame
    {
        public string EventType { get; set; }

        public double ElapsedMs { get; set; }

        public int TotalFingerCount { get; set; }

        public List<TrainingDiagnosticPoint> Points { get; } = new List<TrainingDiagnosticPoint>();
    }

    internal sealed class TrainingDiagnosticPoint
    {
        public int ContactId { get; set; }

        public DeviceStates State { get; set; }

        public int RawX { get; set; }

        public int RawY { get; set; }

        public int TranslatedX { get; set; }

        public int TranslatedY { get; set; }
    }
}
