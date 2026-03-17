using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.Daemon.Input
{
    internal sealed class TipTapMatch
    {
        public int TapId { get; set; }
        public List<int> FixIds { get; set; }
        public List<Point> TapTrajectory { get; set; }
        public List<List<Point>> FixTrajectories { get; set; }
        public ContactGestureDirection Direction { get; set; }
        public Point AnchorPoint { get; set; }
        public Point TapStartPoint { get; set; }
        public Point TapEndPoint { get; set; }
    }

    internal sealed class TipTapRecognizer
    {
        private readonly TipTapRuntimeState _state = new TipTapRuntimeState();

        public TipTapRuntimeState State => _state;

        public void Reset()
        {
            _state.ResetSession();
        }

        public ContactGestureResult ProcessSnapshot(GestureSessionSnapshot session, TipTapGestureConfig config)
        {
            return ProcessSnapshot(session, config, tapId: null, fixIds: null);
        }

        /// <summary>
        /// 使用已知的 tapId 和 fixIds 处理快照。用于 active session 场景，cycle 层已确定身份。
        /// </summary>
        public ContactGestureResult ProcessSnapshot(GestureSessionSnapshot session, TipTapGestureConfig config, int? tapId, IReadOnlyList<int> fixIds)
        {
            if (session == null || config == null || !config.IsEnabled)
                return ContactGestureResult.None;

            if (_state.LastTriggeredAtUtc.HasValue &&
                (DateTime.UtcNow - _state.LastTriggeredAtUtc.Value).TotalMilliseconds < config.Recognition.RepeatCooldownMs)
                return ContactGestureResult.None;

            bool matched;
            TipTapMatch match;
            if (tapId.HasValue && fixIds != null && fixIds.Count > 0)
            {
                matched = TryMatchWithKnownRoles(
                    session, tapId.Value, fixIds,
                    config.Recognition,
                    config.Direction,
                    out match);
            }
            else
            {
                matched = TryMatch(
                    session,
                    config.FingerCount,
                    config.FixFingerCount,
                    config.Recognition,
                    config.Direction,
                    out match);
            }

            if (!matched)
                return ContactGestureResult.None;

            _state.LastTriggeredAtUtc = DateTime.UtcNow;

            return new ContactGestureResult(
                true,
                ContactGestureKind.TipTap,
                config.FingerCount,
                $"fix{config.FixFingerCount}:{match.Direction.ToString().ToLowerInvariant()}");
        }

        /// <summary>
        /// 从 session 的 ContactDownOrder 推断 tapId/fixIds 并匹配。
        /// 用于终端识别场景（GestureClassifier），此时 tapId/fixIds 未预先确定。
        /// </summary>
        internal static bool TryMatch(
            GestureSessionSnapshot session,
            int fingerCount,
            int fixFingerCount,
            TipTapRecognition recognition,
            ContactGestureDirection expectedDirection,
            out TipTapMatch match)
        {
            match = null;
            if (session == null || fingerCount < 2 || fixFingerCount < 1)
                return false;

            if (session.FingerCount > 0 && session.FingerCount != fingerCount)
                return false;

            if (session.ContactDownOrder == null || session.ContactDownOrder.Count < fixFingerCount + 1)
                return false;

            int tapId = session.ContactDownOrder.Last();
            var fixIds = session.ContactDownOrder.Take(session.ContactDownOrder.Count - 1)
                .Where(id => id != tapId)
                .Distinct()
                .Take(fixFingerCount)
                .ToList();
            if (fixIds.Count != fixFingerCount)
                return false;

            return TryMatchWithKnownRoles(
                session, tapId, fixIds, recognition, expectedDirection,
                out match);
        }

        /// <summary>
        /// 使用已知的 tapId 和 fixIds 进行匹配。
        /// 用于 active session 场景（cycle 层已确定 tap/fix 身份）。
        /// </summary>
        internal static bool TryMatchWithKnownRoles(
            GestureSessionSnapshot session,
            int tapId,
            IReadOnlyList<int> fixIds,
            TipTapRecognition recognition,
            ContactGestureDirection expectedDirection,
            out TipTapMatch match)
        {
            match = null;
            if (session == null || fixIds == null || fixIds.Count < 1)
                return false;

            if (session.ContactTrajectories == null || session.ContactTrajectories.Count < fixIds.Count + 1)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: insufficient trajectories: have={session.ContactTrajectories?.Count ?? 0}, need={fixIds.Count + 1}");
                return false;
            }

            recognition ??= new TipTapRecognition();

            var trajectories = session.ContactTrajectories
                .Where(pair => pair.Value != null && pair.Value.Count > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (trajectories.Count < fixIds.Count + 1)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: insufficient non-empty trajectories: have={trajectories.Count}, need={fixIds.Count + 1}");
                return false;
            }

            if (!trajectories.TryGetValue(tapId, out var tapTrajectory) || tapTrajectory == null || tapTrajectory.Count == 0)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: no trajectory for tapId={tapId}");
                return false;
            }

            var fixTrajectories = new List<List<Point>>();
            foreach (int fixId in fixIds)
            {
                if (!trajectories.TryGetValue(fixId, out var fixTrajectory) || fixTrajectory == null || fixTrajectory.Count == 0)
                {
                    Logging.LogTrace($"[TipTapMatch] FAIL: no trajectory for fixId={fixId}");
                    return false;
                }

                fixTrajectories.Add(fixTrajectory);
            }

            bool hasExplicitUpOrder = session.ContactUpOrder != null && session.ContactUpOrder.Count > 0;
            if (hasExplicitUpOrder && session.ContactUpOrder[0] != tapId)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: first up contact={session.ContactUpOrder[0]} is not tapId={tapId}");
                return false;
            }

            if (!hasExplicitUpOrder)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: no explicit up order");
                return false;
            }

            double tapDownMs = 0;
            double tapUpMs = 0;
            bool hasTapDown = session.ContactDownTimesMs != null && session.ContactDownTimesMs.TryGetValue(tapId, out tapDownMs);
            bool hasTapUp = session.ContactUpTimesMs != null && session.ContactUpTimesMs.TryGetValue(tapId, out tapUpMs);
            if (hasTapUp && !hasTapDown)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: tap has up time but no down time");
                return false;
            }

            if (hasTapDown && hasTapUp && tapUpMs - tapDownMs > recognition.MaxTapDurationMs)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: tap duration {tapUpMs - tapDownMs:F0}ms > max {recognition.MaxTapDurationMs}ms");
                return false;
            }

            if (!hasTapDown || !hasTapUp)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: missing tap timing: hasTapDown={hasTapDown}, hasTapUp={hasTapUp}");
                return false;
            }

            foreach (int fixId in fixIds)
            {
                double fixDownMs = 0;
                double fixUpMs = 0;
                bool hasFixDown = session.ContactDownTimesMs != null && session.ContactDownTimesMs.TryGetValue(fixId, out fixDownMs);
                bool hasFixUp = session.ContactUpTimesMs != null && session.ContactUpTimesMs.TryGetValue(fixId, out fixUpMs);

                if (!hasFixDown)
                {
                    Logging.LogTrace($"[TipTapMatch] FAIL: fixId={fixId} has no down time");
                    return false;
                }

                if (hasTapDown && hasFixDown && fixDownMs > tapDownMs)
                {
                    Logging.LogTrace($"[TipTapMatch] FAIL: fixId={fixId} down at {fixDownMs:F0}ms after tap down at {tapDownMs:F0}ms");
                    return false;
                }

                if (hasTapUp && hasFixUp && fixUpMs < tapUpMs)
                {
                    Logging.LogTrace($"[TipTapMatch] FAIL: fixId={fixId} up at {fixUpMs:F0}ms before tap up at {tapUpMs:F0}ms");
                    return false;
                }
            }

            bool hasHoldEvidence = TipTapMath.HasMinimumFixHoldBeforeTap(session, fixIds, tapId, recognition.FixMinHoldMs);
            if (!hasHoldEvidence)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: fix hold time < {recognition.FixMinHoldMs}ms before tap down");
                return false;
            }

            // fix 手指的静止判定由调用方基于 _contactLastMovedTimeMs 完成（IsFixFingersRecentlyStill），
            // 不再用轨迹位移做冗余检查——轨迹包含移动段时会误判

            if (TipTapMath.GetTrajectoryDistance(tapTrajectory) > recognition.TapMaxMovementPx)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: tap moved {TipTapMath.GetTrajectoryDistance(tapTrajectory):F1}px > threshold {recognition.TapMaxMovementPx}px");
                return false;
            }

            var fixXs = fixTrajectories.Select(trajectory => (double)trajectory.Last().X).ToList();
            double anchorX = fixXs.Average();
            ContactGestureDirection actualDirection = TipTapMath.DetermineDirection(fixXs, tapTrajectory.Last().X, recognition.DirectionDeadzonePx);
            if (expectedDirection != ContactGestureDirection.None && expectedDirection != actualDirection)
            {
                Logging.LogTrace($"[TipTapMatch] FAIL: direction mismatch: expected={expectedDirection}, actual={actualDirection}");
                return false;
            }

            match = new TipTapMatch
            {
                TapId = tapId,
                FixIds = fixIds.ToList(),
                TapTrajectory = tapTrajectory,
                FixTrajectories = fixTrajectories,
                Direction = actualDirection,
                AnchorPoint = new Point((int)Math.Round(anchorX), (int)Math.Round(fixTrajectories.Select(trajectory => trajectory.Last().Y).Average())),
                TapStartPoint = tapTrajectory.First(),
                TapEndPoint = tapTrajectory.Last(),
            };
            return true;
        }

    }
}
