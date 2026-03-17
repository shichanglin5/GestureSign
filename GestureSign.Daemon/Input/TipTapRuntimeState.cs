using System;

namespace GestureSign.Daemon.Input
{
    internal sealed class TipTapRuntimeState
    {
        public DateTime? LastTriggeredAtUtc { get; set; }

        public void ResetSession()
        {
            LastTriggeredAtUtc = null;
        }
    }
}
