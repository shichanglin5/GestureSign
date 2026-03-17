using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Common.Log;

namespace GestureSign.Daemon.Input
{
    internal sealed class MultiFingerTapRecognizer : IContactGestureRecognizer
    {
        internal static bool IsMatch(GestureSessionSnapshot session, GestureAnalysis analysis, TapGestureRecognition recognition = null)
        {
            if (session == null || analysis == null)
                return false;

            recognition ??= new TapGestureRecognition();

            int fingerCount = session.FingerCount > 0
                ? session.FingerCount
                : analysis.FingerCount > 0
                    ? analysis.FingerCount
                    : session.ContactTrajectories?.Count ?? session.AllPoints?.Count ?? 0;

            if (fingerCount < recognition.MinFingerCount)
            {
                Logging.LogTrace($"[MultiFingerTap] FAIL: fingerCount={fingerCount} < min={recognition.MinFingerCount}");
                return false;
            }

            if (!AreAllContactsReleased(session))
            {
                Logging.LogTrace($"[MultiFingerTap] FAIL: not all contacts released");
                return false;
            }

            // duration 判定完全由 IsTapLike 统一处理（含多指裕量），
            // 避免此处的 recognition.MaxDurationMs 与 AppConfig.TapMaxDurationMs 不一致
            if (!analysis.IsTapLike)
            {
                double durationMs = GestureSessionTiming.GetEffectiveGestureDurationMs(session, analysis.DurationMs);
                Logging.LogTrace($"[MultiFingerTap] FAIL: not tap-like (maxDist={analysis.MaxPerFingerDistance:F1}px, avgDist={analysis.AveragePerFingerDistance:F1}px, duration={durationMs:F0}ms, fingers={fingerCount})");
                return false;
            }

            return true;
        }

        private static bool AreAllContactsReleased(GestureSessionSnapshot session)
        {
            return session?.ActiveContactIds == null || session.ActiveContactIds.Count == 0;
        }

        public ContactGestureResult TryRecognize(GestureSessionSnapshot session, GestureAnalysis analysis)
        {
            if (!IsMatch(session, analysis))
                return ContactGestureResult.None;

            int fingerCount = session.FingerCount > 0 ? session.FingerCount : analysis.FingerCount;
            return new ContactGestureResult(true, ContactGestureKind.MultiFingerTap, fingerCount, $"{fingerCount}-finger-tap");
        }
    }
}
