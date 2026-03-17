using GestureSign.Common.Input;
using System.Linq;

namespace GestureSign.Daemon.Input
{
    internal static class GestureSessionTiming
    {
        public static double GetEffectiveGestureDurationMs(GestureSessionSnapshot session, double fallbackDurationMs = 0)
        {
            if (session == null)
                return fallbackDurationMs;

            var downTimes = session.ContactDownTimesMs?.Values.ToList();
            if (downTimes == null || downTimes.Count == 0)
                return session.DurationMs > 0 ? session.DurationMs : fallbackDurationMs;

            double firstDownMs = downTimes.Min();

            double lastObservedMs = 0;
            if (session.ContactUpTimesMs != null && session.ContactUpTimesMs.Count > 0)
            {
                lastObservedMs = session.ContactUpTimesMs.Values
                    .Where(value => value >= firstDownMs)
                    .DefaultIfEmpty(0)
                    .Max();
            }

            if (lastObservedMs <= 0)
                lastObservedMs = session.DurationMs;

            if (lastObservedMs <= firstDownMs)
                return session.DurationMs > 0 ? session.DurationMs : fallbackDurationMs;

            return lastObservedMs - firstDownMs;
        }
    }
}
