using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Daemon.Input
{
    internal static class GestureClassifier
    {
        private const int TipTapTapHardLimitMs = 220;
        private const int TipTapMinMaxDurationMs = 140;

        public static RecordedGestureType Classify(RecordedGestureSample sample)
        {
            if (sample == null || sample.Analysis == null)
                return RecordedGestureType.Unknown;

            if (sample.FingerCount >= 2 && LooksLikeClick(sample))
                return RecordedGestureType.Click;

            if (sample.FingerCount >= 2 && LooksLikeTap(sample))
                return RecordedGestureType.Tap;

            if (sample.FingerCount >= 2 && LooksLikeTipTap(sample))
                return RecordedGestureType.TipTap;

            return RecordedGestureType.Trajectory;
        }

        private static bool LooksLikeClick(RecordedGestureSample sample)
        {
            if (sample?.Session == null || sample.Analysis == null)
                return false;

            // Click 手势因为按下物理按键时触摸板表面形变，手指移动量天然更大，
            // 所以使用基于实际观测数据的宽松阈值（与 CreateClickDefinition 一致）。
            double maxMovementPx = System.Math.Max(50, (sample.Analysis?.MaxPerFingerDistance ?? TapGestureRecognition.DefaultMaxMovementPx) + 10);
            double pressDisplacement = sample.Analysis?.MaxPerFingerDistance ?? 0;
            return MultiFingerClickRecognizer.IsMatch(
                sample.Session,
                pressDisplacement,
                new ClickGestureRecognition { MinFingerCount = sample.FingerCount, MaxMovementPx = maxMovementPx });
        }

        private static bool LooksLikeTap(RecordedGestureSample sample)
        {
            if (sample?.Session == null || sample.Analysis == null)
                return false;

            return MultiFingerTapRecognizer.IsMatch(
                sample.Session,
                sample.Analysis,
                new TapGestureRecognition { MinFingerCount = sample.FingerCount });
        }

        private static bool LooksLikeTipTap(RecordedGestureSample sample)
        {
            return TryGetTipTapPartition(sample, out _, out _, out _);
        }

        public static TapGestureConfig CreateTapDefinition(RecordedGestureSample sample)
        {
            return new TapGestureConfig
            {
                Id = System.Guid.NewGuid().ToString("N"),
                Name = ContactGestureText.GetTapName(sample.FingerCount),
                FingerCount = sample.FingerCount,
                Recognition = new TapGestureRecognition
                {
                    MaxDurationMs = (int)System.Math.Ceiling(sample.DurationMs > 0 ? sample.DurationMs : TapGestureRecognition.DefaultMaxDurationMs),
                    MaxMovementPx = System.Math.Max(TapGestureRecognition.DefaultMaxMovementPx, sample.Analysis?.MaxPerFingerDistance ?? TapGestureRecognition.DefaultMaxMovementPx),
                    MinFingerCount = sample.FingerCount,
                }
            };
        }

        public static ClickGestureConfig CreateClickDefinition(RecordedGestureSample sample)
        {
            int fingerCount = sample?.Session?.PrimaryButtonFingerCount > 0
                ? sample.Session.PrimaryButtonFingerCount
                : sample?.FingerCount ?? 2;
            double pressDurationMs = MultiFingerClickRecognizer.GetPressDurationMs(sample?.Session);

            return new ClickGestureConfig
            {
                Id = System.Guid.NewGuid().ToString("N"),
                Name = ContactGestureText.GetClickName(fingerCount),
                FingerCount = fingerCount,
                Recognition = new ClickGestureRecognition
                {
                    MaxPressDurationMs = (int)System.Math.Ceiling(pressDurationMs > 0 ? pressDurationMs : ClickGestureRecognition.DefaultMaxPressDurationMs),
                    MaxMovementPx = System.Math.Max(ClickGestureRecognition.DefaultMaxMovementPx, sample?.Analysis?.MaxPerFingerDistance ?? ClickGestureRecognition.DefaultMaxMovementPx),
                    MinFingerCount = fingerCount,
                }
            };
        }

        public static TipTapGestureConfig CreateTipTapDefinition(RecordedGestureSample sample)
        {
            if (!TryGetTipTapPartition(sample, out var fixTrajectories, out var tapTrajectory, out int fixFingerCount))
            {
                fixFingerCount = System.Math.Max(1, sample.FingerCount - 1);
                var fallbackDirection = DetectTipTapDirection(sample, fixFingerCount);

                return new TipTapGestureConfig
                {
                    Id = System.Guid.NewGuid().ToString("N"),
                    Name = ContactGestureText.GetTipTapDetailedName(fallbackDirection, fixFingerCount),
                    FingerCount = sample.FingerCount,
                    FixFingerCount = fixFingerCount,
                    Direction = fallbackDirection,
                    Recognition = CreateTipTapRecognition(sample, null, null),
                };
            }

            var direction = DetectTipTapDirection(fixTrajectories, tapTrajectory);

            return new TipTapGestureConfig
            {
                Id = System.Guid.NewGuid().ToString("N"),
                Name = ContactGestureText.GetTipTapDetailedName(direction, fixFingerCount),
                FingerCount = sample.FingerCount,
                FixFingerCount = fixFingerCount,
                Direction = direction,
                Recognition = CreateTipTapRecognition(sample, fixTrajectories, tapTrajectory),
            };
        }

        private static ContactGestureDirection DetectTipTapDirection(RecordedGestureSample sample, int fixFingerCount)
        {
            if (sample?.Session?.ContactTrajectories == null || sample.Session.ContactTrajectories.Count < fixFingerCount + 1)
                return ContactGestureDirection.None;

            var ordered = sample.Session.ContactTrajectories
                .Where(kvp => kvp.Value != null && kvp.Value.Count > 0)
                .Select(kvp => new
                {
                    kvp.Key,
                    Trajectory = kvp.Value,
                    Distance = TipTapMath.GetTrajectoryDistance(kvp.Value)
                })
                .OrderBy(x => x.Distance)
                .ToList();

            if (ordered.Count < fixFingerCount + 1)
                return ContactGestureDirection.None;

            var fix = ordered.Take(fixFingerCount).ToList();
            var tap = ordered.Skip(fixFingerCount).OrderByDescending(x => x.Distance).FirstOrDefault();
            if (tap == null)
                return ContactGestureDirection.None;

            return TipTapMath.DetermineDirection(
                fix.Select(item => item.Trajectory.Average(point => point.X)),
                tap.Trajectory.Average(point => point.X),
                TipTapRecognition.DefaultDirectionDeadzonePx);
        }

        private static bool TryGetTipTapPartition(
            RecordedGestureSample sample,
            out List<ContactTrajectoryInfo> fixTrajectories,
            out ContactTrajectoryInfo tapTrajectory,
            out int fixFingerCount)
        {
            fixTrajectories = null;
            tapTrajectory = null;
            fixFingerCount = 0;

            if (sample?.Session?.ContactTrajectories == null || sample.FingerCount < 2)
                return false;

            fixFingerCount = sample.FingerCount - 1;
            if (TryGetTipTapPartitionFromSharedMatcher(sample, out fixTrajectories, out tapTrajectory, fixFingerCount))
                return true;

            if (HasTipTapOrderingMetadata(sample))
            {
                if (TryGetTipTapPartitionFromActiveFixFingers(sample, out fixTrajectories, out tapTrajectory, out fixFingerCount))
                    return true;

                if (TryGetTipTapPartitionFromTiming(sample, out fixTrajectories, out tapTrajectory, out fixFingerCount))
                    return true;

                if (TryGetTipTapPartitionFromDownOrder(sample, out fixTrajectories, out tapTrajectory, out fixFingerCount))
                    return true;

                return false;
            }

            var trajectories = sample.Session.ContactTrajectories
                .Where(kvp => kvp.Value != null && kvp.Value.Count > 0)
                .Select(kvp => new ContactTrajectoryInfo
                {
                    Id = kvp.Key,
                    Trajectory = kvp.Value,
                    PointCount = kvp.Value.Count,
                    Distance = TipTapMath.GetTrajectoryDistance(kvp.Value),
                    AverageX = kvp.Value.Average(point => point.X),
                    AverageY = kvp.Value.Average(point => point.Y),
                })
                .ToList();

            if (trajectories.Count < sample.FingerCount)
                return false;

            fixFingerCount = sample.FingerCount - 1;
            var ordered = trajectories
                .OrderByDescending(info => info.PointCount)
                .ThenBy(info => info.Distance)
                .ToList();

            fixTrajectories = ordered.Take(fixFingerCount).ToList();
            tapTrajectory = ordered.Skip(fixFingerCount)
                .OrderBy(info => info.PointCount)
                .ThenBy(info => info.Distance)
                .FirstOrDefault();

            if (fixTrajectories.Count != fixFingerCount || tapTrajectory == null)
                return false;

            bool fixIsStable = fixTrajectories.All(info => info.Distance <= GetTipTapFixStabilityThreshold(sample, info.Distance));
            bool tapIsTapLike = tapTrajectory.Distance <= GetTipTapTapMovementThreshold(sample, tapTrajectory.Distance);
            int tapPointCount = tapTrajectory.PointCount;
            bool tapArrivedLater = fixTrajectories.All(info => info.PointCount > tapPointCount);

            return fixIsStable && tapIsTapLike && tapArrivedLater;
        }

        private static bool TryGetTipTapPartitionFromSharedMatcher(
            RecordedGestureSample sample,
            out List<ContactTrajectoryInfo> fixTrajectories,
            out ContactTrajectoryInfo tapTrajectory,
            int fixFingerCount)
        {
            fixTrajectories = null;
            tapTrajectory = null;

            if (!TipTapRecognizer.TryMatch(
                sample?.Session,
                sample?.FingerCount ?? 0,
                fixFingerCount,
                CreateTrainingTipTapRecognition(sample),
                ContactGestureDirection.None,
                out var match))
            {
                return false;
            }

            fixTrajectories = match.FixIds.Select((fixId, index) =>
            {
                var fixPoints = match.FixTrajectories[index];
                double fixDownMs = 0;
                double fixUpMs = 0;
                sample.Session.ContactDownTimesMs?.TryGetValue(fixId, out fixDownMs);
                sample.Session.ContactUpTimesMs?.TryGetValue(fixId, out fixUpMs);

                return new ContactTrajectoryInfo
                {
                    Id = fixId,
                    Trajectory = fixPoints,
                    PointCount = fixPoints.Count,
                    Distance = TipTapMath.GetTrajectoryDistance(fixPoints),
                    AverageX = fixPoints.Average(point => point.X),
                    AverageY = fixPoints.Average(point => point.Y),
                    DownTimeMs = fixDownMs,
                    UpTimeMs = fixUpMs,
                };
            }).ToList();

            double tapDownMs = 0;
            double tapUpMs = 0;
            sample.Session.ContactDownTimesMs?.TryGetValue(match.TapId, out tapDownMs);
            sample.Session.ContactUpTimesMs?.TryGetValue(match.TapId, out tapUpMs);
            tapTrajectory = new ContactTrajectoryInfo
            {
                Id = match.TapId,
                Trajectory = match.TapTrajectory,
                PointCount = match.TapTrajectory.Count,
                Distance = TipTapMath.GetTrajectoryDistance(match.TapTrajectory),
                AverageX = match.TapTrajectory.Average(point => point.X),
                AverageY = match.TapTrajectory.Average(point => point.Y),
                DownTimeMs = tapDownMs,
                UpTimeMs = tapUpMs,
            };

            return true;
        }

        private static bool HasTipTapOrderingMetadata(RecordedGestureSample sample)
        {
            var session = sample?.Session;
            if (session == null)
                return false;

            return (session.ContactDownOrder != null && session.ContactDownOrder.Count > 0)
                || (session.ContactUpOrder != null && session.ContactUpOrder.Count > 0)
                || (session.ContactDownTimesMs != null && session.ContactDownTimesMs.Count > 0)
                || (session.ContactUpTimesMs != null && session.ContactUpTimesMs.Count > 0)
                || (session.ActiveContactIds != null && session.ActiveContactIds.Count > 0);
        }

        private static TipTapRecognition CreateTrainingTipTapRecognition(RecordedGestureSample sample)
        {
            double observedFixDistance = sample?.Analysis?.MaxPerFingerDistance ?? sample?.Analysis?.AveragePerFingerDistance ?? 0;
            double observedTapDistance = sample?.Analysis?.MaxPerFingerDistance ?? 0;

            return new TipTapRecognition
            {
                FixMinHoldMs = AppConfig.TipTapFixMinHoldMs,
                MaxTapDurationMs = AppConfig.TipTapMaxTapDurationMs,
                FixStillThresholdPx = GetTipTapFixStabilityThreshold(sample, observedFixDistance),
                TapMaxMovementPx = GetTipTapTapMovementThreshold(sample, observedTapDistance),
                DirectionDeadzonePx = TipTapRecognition.DefaultDirectionDeadzonePx,
                RepeatCooldownMs = TipTapRecognition.DefaultRepeatCooldownMs,
            };
        }

        private static bool TryGetTipTapPartitionFromActiveFixFingers(
            RecordedGestureSample sample,
            out List<ContactTrajectoryInfo> fixTrajectories,
            out ContactTrajectoryInfo tapTrajectory,
            out int fixFingerCount)
        {
            fixTrajectories = null;
            tapTrajectory = null;
            fixFingerCount = 0;

            var session = sample?.Session;
            if (session?.ContactTrajectories == null || session.ActiveContactIds == null || session.ActiveContactIds.Count == 0)
                return false;

            var activeFixIds = session.ActiveContactIds
                .Where(id => session.ContactTrajectories.TryGetValue(id, out var points) && points != null && points.Count > 0)
                .Distinct()
                .ToList();
            if (activeFixIds.Count == 0 || activeFixIds.Count >= sample.FingerCount)
                return false;

            if (session.ContactUpOrder == null || session.ContactUpOrder.Count == 0)
                return false;

            if (session.ContactDownOrder == null || session.ContactDownOrder.Count < sample.FingerCount)
                return false;

            var activeFixSet = activeFixIds.ToHashSet();
            int tapId = session.ContactUpOrder.FirstOrDefault(id => !activeFixSet.Contains(id));
            if (!session.ContactTrajectories.TryGetValue(tapId, out var tapPoints) || tapPoints == null || tapPoints.Count == 0)
                return false;

            if (session.ContactUpOrder[0] != tapId)
                return false;

            // This fallback only exists for recordings where the fixed fingers stay down
            // and the tap finger is the last arrival but timing metadata is incomplete.
            // If the released finger arrived before the remaining active fingers, it is much
            // more likely a rolled multi-finger tap than a real TipTap.
            if (session.ContactDownOrder.Last() != tapId)
                return false;

            if (session.ContactDownTimesMs.TryGetValue(tapId, out double tapDownMsForOrdering))
            {
                foreach (int activeFixId in activeFixIds)
                {
                    if (session.ContactDownTimesMs.TryGetValue(activeFixId, out double fixDownMs) && fixDownMs > tapDownMsForOrdering)
                        return false;
                }
            }

            fixFingerCount = activeFixIds.Count;
            fixTrajectories = activeFixIds.Select(fixId =>
            {
                var fixPoints = session.ContactTrajectories[fixId];
                session.ContactDownTimesMs.TryGetValue(fixId, out double fixDownMs);
                session.ContactUpTimesMs.TryGetValue(fixId, out double fixUpMs);

                return new ContactTrajectoryInfo
                {
                    Id = fixId,
                    Trajectory = fixPoints,
                    PointCount = fixPoints.Count,
                    Distance = TipTapMath.GetTrajectoryDistance(fixPoints),
                    AverageX = fixPoints.Average(point => point.X),
                    AverageY = fixPoints.Average(point => point.Y),
                    DownTimeMs = fixDownMs,
                    UpTimeMs = fixUpMs,
                };
            }).ToList();

            session.ContactDownTimesMs.TryGetValue(tapId, out double tapDownMs);
            session.ContactUpTimesMs.TryGetValue(tapId, out double tapUpMs);
            tapTrajectory = new ContactTrajectoryInfo
            {
                Id = tapId,
                Trajectory = tapPoints,
                PointCount = tapPoints.Count,
                Distance = TipTapMath.GetTrajectoryDistance(tapPoints),
                AverageX = tapPoints.Average(point => point.X),
                AverageY = tapPoints.Average(point => point.Y),
                DownTimeMs = tapDownMs,
                UpTimeMs = tapUpMs,
            };

            if (tapTrajectory.UpTimeMs > tapTrajectory.DownTimeMs && tapTrajectory.UpTimeMs - tapTrajectory.DownTimeMs > TipTapTapHardLimitMs)
                return false;

            bool fixIsStable = fixTrajectories.All(info => info.Distance <= GetTipTapFixStabilityThreshold(sample, info.Distance));
            bool tapIsTapLike = tapTrajectory.Distance <= GetTipTapTapMovementThreshold(sample, tapTrajectory.Distance);
            bool hasHoldEvidence = TipTapMath.HasMinimumFixHoldBeforeTap(session, activeFixIds, tapId, AppConfig.TipTapFixMinHoldMs);
            return fixIsStable && tapIsTapLike && hasHoldEvidence;
        }

        private static bool TryGetTipTapPartitionFromDownOrder(
            RecordedGestureSample sample,
            out List<ContactTrajectoryInfo> fixTrajectories,
            out ContactTrajectoryInfo tapTrajectory,
            out int fixFingerCount)
        {
            fixTrajectories = null;
            tapTrajectory = null;
            fixFingerCount = 0;

            var session = sample?.Session;
            if (session?.ContactTrajectories == null || session.ContactDownOrder == null || session.ContactDownOrder.Count < sample.FingerCount)
                return false;

            int tapId = session.ContactDownOrder.Last();
            if (!session.ContactTrajectories.TryGetValue(tapId, out var tapPoints) || tapPoints == null || tapPoints.Count == 0)
                return false;

            if (session.ContactUpOrder != null && session.ContactUpOrder.Count > 0)
            {
                if (session.ContactUpOrder[0] != tapId)
                    return false;
            }

            fixFingerCount = sample.FingerCount - 1;
            var fixIds = session.ContactDownOrder.Take(session.ContactDownOrder.Count - 1)
                .Where(id => id != tapId)
                .Distinct()
                .Take(fixFingerCount)
                .ToList();

            if (fixIds.Count != fixFingerCount)
                return false;

            if (!TipTapMath.HasMinimumFixHoldBeforeTap(session, fixIds, tapId, AppConfig.TipTapFixMinHoldMs))
                return false;

            if (session.ContactDownTimesMs.TryGetValue(tapId, out double tapDownMs))
            {
                foreach (int fixId in fixIds)
                {
                    if (session.ContactDownTimesMs.TryGetValue(fixId, out double fixDownMs) && fixDownMs > tapDownMs)
                        return false;
                }
            }

            fixTrajectories = fixIds.Select(fixId =>
            {
                var fixPoints = session.ContactTrajectories[fixId];
                session.ContactDownTimesMs.TryGetValue(fixId, out double fixDownMs);
                session.ContactUpTimesMs.TryGetValue(fixId, out double fixUpMs);

                return new ContactTrajectoryInfo
                {
                    Id = fixId,
                    Trajectory = fixPoints,
                    PointCount = fixPoints.Count,
                    Distance = TipTapMath.GetTrajectoryDistance(fixPoints),
                    AverageX = fixPoints.Average(point => point.X),
                    AverageY = fixPoints.Average(point => point.Y),
                    DownTimeMs = fixDownMs,
                    UpTimeMs = fixUpMs,
                };
            }).ToList();

            session.ContactDownTimesMs.TryGetValue(tapId, out double tapRecordedDownMs);
            session.ContactUpTimesMs.TryGetValue(tapId, out double tapRecordedUpMs);
            tapTrajectory = new ContactTrajectoryInfo
            {
                Id = tapId,
                Trajectory = tapPoints,
                PointCount = tapPoints.Count,
                Distance = TipTapMath.GetTrajectoryDistance(tapPoints),
                AverageX = tapPoints.Average(point => point.X),
                AverageY = tapPoints.Average(point => point.Y),
                DownTimeMs = tapRecordedDownMs,
                UpTimeMs = tapRecordedUpMs,
            };

            bool fixIsStable = fixTrajectories.All(info => info.Distance <= GetTipTapFixStabilityThreshold(sample, info.Distance));
            bool tapIsTapLike = tapTrajectory.Distance <= GetTipTapTapMovementThreshold(sample, tapTrajectory.Distance);
            return fixIsStable && tapIsTapLike;
        }

        private static bool TryGetTipTapPartitionFromTiming(
            RecordedGestureSample sample,
            out List<ContactTrajectoryInfo> fixTrajectories,
            out ContactTrajectoryInfo tapTrajectory,
            out int fixFingerCount)
        {
            fixTrajectories = null;
            tapTrajectory = null;
            fixFingerCount = 0;

            var session = sample?.Session;
            if (session?.ContactTrajectories == null || session.ContactDownOrder == null || session.ContactDownOrder.Count < sample.FingerCount)
                return false;

            int tapId = session.ContactDownOrder.LastOrDefault();
            if (tapId == 0 && !session.ContactTrajectories.ContainsKey(tapId))
                return false;

            if (session.ContactUpOrder == null || session.ContactUpOrder.Count == 0 || session.ContactUpOrder[0] != tapId)
                return false;

            if (!session.ContactTrajectories.TryGetValue(tapId, out var tapPoints) || tapPoints == null || tapPoints.Count == 0)
                return false;

            if (!session.ContactDownTimesMs.TryGetValue(tapId, out double tapDownMs) ||
                !session.ContactUpTimesMs.TryGetValue(tapId, out double tapUpMs))
                return false;

            double tapDurationMs = tapUpMs - tapDownMs;
            if (tapDurationMs <= 0 || tapDurationMs > TipTapTapHardLimitMs)
                return false;

            fixFingerCount = sample.FingerCount - 1;
            var fixIds = session.ContactDownOrder.Take(session.ContactDownOrder.Count - 1)
                .Where(id => id != tapId)
                .Distinct()
                .Take(fixFingerCount)
                .ToList();

            if (fixIds.Count != fixFingerCount)
                return false;

            if (!TipTapMath.HasMinimumFixHoldBeforeTap(session, fixIds, tapId, AppConfig.TipTapFixMinHoldMs))
                return false;

            foreach (int fixId in fixIds)
            {
                if (!session.ContactTrajectories.TryGetValue(fixId, out var fixPoints) || fixPoints == null || fixPoints.Count == 0)
                    return false;

                if (!session.ContactDownTimesMs.TryGetValue(fixId, out double fixDownMs) || fixDownMs > tapDownMs)
                    return false;

                if (session.ContactUpTimesMs.TryGetValue(fixId, out double fixUpMs) && fixUpMs < tapUpMs)
                    return false;
            }

            tapTrajectory = new ContactTrajectoryInfo
            {
                Id = tapId,
                Trajectory = tapPoints,
                PointCount = tapPoints.Count,
                Distance = TipTapMath.GetTrajectoryDistance(tapPoints),
                AverageX = tapPoints.Average(point => point.X),
                AverageY = tapPoints.Average(point => point.Y),
                DownTimeMs = tapDownMs,
                UpTimeMs = tapUpMs,
            };

            fixTrajectories = fixIds.Select(fixId =>
            {
                var fixPoints = session.ContactTrajectories[fixId];
                session.ContactDownTimesMs.TryGetValue(fixId, out double fixDownMs);
                session.ContactUpTimesMs.TryGetValue(fixId, out double fixUpMs);

                return new ContactTrajectoryInfo
                {
                    Id = fixId,
                    Trajectory = fixPoints,
                    PointCount = fixPoints.Count,
                    Distance = TipTapMath.GetTrajectoryDistance(fixPoints),
                    AverageX = fixPoints.Average(point => point.X),
                    AverageY = fixPoints.Average(point => point.Y),
                    DownTimeMs = fixDownMs,
                    UpTimeMs = fixUpMs,
                };
            }).ToList();

            bool fixIsStable = fixTrajectories.All(info => info.Distance <= GetTipTapFixStabilityThreshold(sample, info.Distance));
            bool tapIsTapLike = tapTrajectory.Distance <= GetTipTapTapMovementThreshold(sample, tapTrajectory.Distance);
            return fixIsStable && tapIsTapLike;
        }

        private static TipTapRecognition CreateTipTapRecognition(
            RecordedGestureSample sample,
            List<ContactTrajectoryInfo> fixTrajectories,
            ContactTrajectoryInfo tapTrajectory)
        {
            double fixDistance = fixTrajectories?.Count > 0 ? fixTrajectories.Max(info => info.Distance) : 0;
            double tapDistance = tapTrajectory?.Distance ?? sample?.Analysis?.MaxPerFingerDistance ?? 0;

            return new TipTapRecognition
            {
                FixMinHoldMs = AppConfig.TipTapFixMinHoldMs,
                MaxTapDurationMs = AppConfig.TipTapMaxTapDurationMs,
                FixStillThresholdPx = System.Math.Max(TipTapRecognition.DefaultFixStillThresholdPx, System.Math.Ceiling(fixDistance) + 2),
                TapMaxMovementPx = GetTipTapTapMovementThreshold(sample, tapDistance),
                DirectionDeadzonePx = TipTapRecognition.DefaultDirectionDeadzonePx,
                RepeatCooldownMs = TipTapRecognition.DefaultRepeatCooldownMs,
            };
        }

        private static bool HasMinimumFixHoldBeforeTap(
            GestureSessionSnapshot session,
            IReadOnlyList<int> fixIds,
            int tapId,
            int minFixHoldMs)
        {
            if (session?.ContactDownTimesMs == null || fixIds == null || fixIds.Count == 0)
                return false;

            if (!session.ContactDownTimesMs.TryGetValue(tapId, out double tapDownMs))
                return false;

            double latestFixDownMs = double.MinValue;
            foreach (int fixId in fixIds)
            {
                if (!session.ContactDownTimesMs.TryGetValue(fixId, out double fixDownMs))
                    return false;

                latestFixDownMs = System.Math.Max(latestFixDownMs, fixDownMs);
            }

            return tapDownMs - latestFixDownMs >= minFixHoldMs;
        }

        private static int GetTipTapMaxDurationMs(ContactTrajectoryInfo tapTrajectory)
        {
            if (tapTrajectory != null && tapTrajectory.UpTimeMs > tapTrajectory.DownTimeMs)
                return (int)System.Math.Max(TipTapMinMaxDurationMs, System.Math.Ceiling(tapTrajectory.UpTimeMs - tapTrajectory.DownTimeMs + 40));

            return TipTapMinMaxDurationMs;
        }

        private static double GetTipTapTapMovementThreshold(RecordedGestureSample sample, double observedDistance)
        {
            double configuredTapThreshold = System.Math.Max(TapGestureRecognition.DefaultMaxMovementPx, AppConfig.TapDistanceThreshold);
            double sampleTapDistance = sample?.Analysis?.MaxPerFingerDistance ?? 0;
            return System.Math.Max(configuredTapThreshold, System.Math.Ceiling(System.Math.Max(observedDistance, sampleTapDistance)));
        }

        private static double GetTipTapFixStabilityThreshold(RecordedGestureSample sample, double observedDistance)
        {
            double configuredThreshold = System.Math.Max(TipTapRecognition.DefaultFixStillThresholdPx, AppConfig.TapDistanceThreshold / 2.0);
            double sampleAverageDistance = sample?.Analysis?.AveragePerFingerDistance ?? 0;
            return System.Math.Max(configuredThreshold, System.Math.Ceiling(System.Math.Max(observedDistance, sampleAverageDistance)));
        }

        private static ContactGestureDirection DetectTipTapDirection(List<ContactTrajectoryInfo> fixTrajectories, ContactTrajectoryInfo tapTrajectory)
        {
            if (fixTrajectories == null || fixTrajectories.Count == 0 || tapTrajectory == null)
                return ContactGestureDirection.None;

            return TipTapMath.DetermineDirection(
                fixTrajectories.Select(info => info.AverageX),
                tapTrajectory.AverageX,
                TipTapRecognition.DefaultDirectionDeadzonePx);
        }

        private sealed class ContactTrajectoryInfo
        {
            public int Id { get; set; }
            public List<System.Drawing.Point> Trajectory { get; set; }
            public int PointCount { get; set; }
            public double Distance { get; set; }
            public double AverageX { get; set; }
            public double AverageY { get; set; }
            public double DownTimeMs { get; set; }
            public double UpTimeMs { get; set; }
        }

    }
}

