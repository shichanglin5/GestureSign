using GestureSign.Common.Input;

namespace GestureSign.Daemon.Input
{
    internal interface IContactGestureRecognizer
    {
        ContactGestureResult TryRecognize(GestureSessionSnapshot session, GestureAnalysis analysis);
    }
}
