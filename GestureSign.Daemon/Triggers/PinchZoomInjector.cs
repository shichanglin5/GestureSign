using GestureSign.Daemon.Native;
using System.Drawing;

namespace GestureSign.Daemon.Triggers
{
    class PinchZoomInjector
    {
        private bool _active;
        private POINT _center;
        private double _currentOffset;
        private const double InitialOffset = 100.0;
        private const double MinOffset = 10.0;
        private const int ContactId0 = 100;
        private const int ContactId1 = 101;
        private static bool _initialized;

        public bool IsActive => _active;

        public void Start(Point center)
        {
            EnsureInitialized();
            _center = new POINT { X = center.X, Y = center.Y };
            _currentOffset = InitialOffset;
            _active = true;

            var contacts = CreateContactPair(POINTER_FLAGS.DOWN | POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT);
            NativeMethods.InjectTouchInput(2, contacts);
        }

        public void Update(double distDelta, double zoomSpeed)
        {
            if (!_active) return;

            double effectiveSpeed = zoomSpeed <= 0 ? 1.0 : zoomSpeed;
            _currentOffset += distDelta * effectiveSpeed;
            if (_currentOffset < MinOffset)
                _currentOffset = MinOffset;

            var contacts = CreateContactPair(POINTER_FLAGS.UPDATE | POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT);
            NativeMethods.InjectTouchInput(2, contacts);
        }

        public void Stop()
        {
            if (!_active) return;
            _active = false;

            var contacts = CreateContactPair(POINTER_FLAGS.UP);
            NativeMethods.InjectTouchInput(2, contacts);
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            // May already be initialized by PointerInputTargetWindow; ignore failure
            NativeMethods.InitializeTouchInjection(10, TOUCH_FEEDBACK.NONE);
            _initialized = true;
        }

        private POINTER_TOUCH_INFO[] CreateContactPair(POINTER_FLAGS flags)
        {
            int offsetX = (int)_currentOffset;

            return new[]
            {
                CreateContact(ContactId0, _center.X - offsetX, _center.Y, flags),
                CreateContact(ContactId1, _center.X + offsetX, _center.Y, flags),
            };
        }

        private static POINTER_TOUCH_INFO CreateContact(int id, int x, int y, POINTER_FLAGS flags)
        {
            return new POINTER_TOUCH_INFO
            {
                PointerInfo = new POINTER_INFO
                {
                    pointerType = POINTER_INPUT_TYPE.TOUCH,
                    PointerID = id,
                    PointerFlags = flags,
                    PtPixelLocation = new POINT { X = x, Y = y },
                },
                TouchFlags = TOUCH_FLAGS.NONE,
                TouchMask = TOUCH_MASK.CONTACTAREA | TOUCH_MASK.PRESSURE,
                ContactArea = new RECT(x - 2, y - 2, 4, 4),
                Pressure = 512,
            };
        }
    }
}
