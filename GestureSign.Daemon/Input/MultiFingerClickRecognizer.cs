using GestureSign.Common.Applications;
using GestureSign.Common.Input;

namespace GestureSign.Daemon.Input
{
    internal sealed class MultiFingerClickRecognizer : IContactGestureRecognizer
    {
        /// <summary>
        /// 判断是否匹配 Click 手势。
        /// pressDisplacement 为按钮按下期间各手指的最大位移（按下时位置 vs 释放时位置）。
        /// </summary>
        internal static bool IsMatch(GestureSessionSnapshot session, double pressDisplacement, ClickGestureRecognition recognition = null)
        {
            if (session == null)
                return false;

            recognition ??= new ClickGestureRecognition();

            if (!session.HasPrimaryButtonClick)
                return false;

            int fingerCount = session.PrimaryButtonFingerCount > 0
                ? session.PrimaryButtonFingerCount
                : session.FingerCount;
            if (fingerCount < recognition.MinFingerCount)
                return false;

            double pressDurationMs = GetPressDurationMs(session);
            if (pressDurationMs > recognition.MaxPressDurationMs)
                return false;

            return pressDisplacement <= recognition.MaxMovementPx;
        }

        public ContactGestureResult TryRecognize(GestureSessionSnapshot session, GestureAnalysis analysis)
        {
            // 终端分类 fallback：没有按压期间位移数据，用 analysis.MaxPerFingerDistance 近似
            double pressDisplacement = analysis?.MaxPerFingerDistance ?? 0;
            if (!IsMatch(session, pressDisplacement))
                return ContactGestureResult.None;

            int fingerCount = session.PrimaryButtonFingerCount > 0 ? session.PrimaryButtonFingerCount : session.FingerCount;
            return new ContactGestureResult(true, ContactGestureKind.MultiFingerClick, fingerCount, $"{fingerCount}-finger-click");
        }

        internal static double GetPressDurationMs(GestureSessionSnapshot session)
        {
            if (session == null || !session.PrimaryButtonDownTimeMs.HasValue)
                return session?.DurationMs ?? 0;

            double upTimeMs = session.PrimaryButtonUpTimeMs ?? session.DurationMs;
            return upTimeMs - session.PrimaryButtonDownTimeMs.Value;
        }
    }
}
