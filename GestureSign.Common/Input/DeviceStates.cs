using System;

namespace GestureSign.Common.Input
{
    [Flags]
    public enum DeviceStates
    {
        None = 0,
        Tip = 1 << 0,
        InRange = 1 << 1,
        PrimaryButton = 1 << 2,
        RightClickButton = 1 << 5,
        Invert = 1 << 3,
        Eraser = 1 << 4,
    }
}
