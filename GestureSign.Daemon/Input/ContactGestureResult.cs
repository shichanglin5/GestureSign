namespace GestureSign.Daemon.Input
{
    internal enum ContactGestureKind
    {
        None = 0,
        MultiFingerTap = 1,
        TipTap = 2,
        MultiFingerClick = 3,
    }

    internal sealed class ContactGestureResult
    {
        public static ContactGestureResult None { get; } = new ContactGestureResult(false, ContactGestureKind.None, 0, null);

        public ContactGestureResult(bool isMatch, ContactGestureKind kind, int fingerCount, string variant)
        {
            IsMatch = isMatch;
            Kind = kind;
            FingerCount = fingerCount;
            Variant = variant;
        }

        public bool IsMatch { get; }
        public ContactGestureKind Kind { get; }
        public int FingerCount { get; }
        public string Variant { get; }
    }
}
