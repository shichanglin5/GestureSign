using System;

namespace GestureSign.Common.Gestures
{
    [Flags]
    public enum GestureModifiers
    {
        Default = 0,
        PrimaryButtonDown = 1,
        Ctrl = 2,
        Shift = 4,
        Alt = 8
    }
}
