using GestureSign.Common.Input;
using System.Drawing;

namespace GestureSign.Daemon.Input
{
    public struct InputPoint
    {
        public InputPoint(int contactIdentifier, Point point)
            : this(contactIdentifier, point, DeviceStates.Tip)
        {
        }

        public InputPoint(int contactIdentifier, Point point, DeviceStates state)
        {
            ContactIdentifier = contactIdentifier;
            Point = point;
            State = state;
        }

        public int ContactIdentifier;
        public Point Point;
        public DeviceStates State;
    }
}
