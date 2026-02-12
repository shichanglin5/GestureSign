using GestureSign.Common.Log;
using GestureSign.Daemon.Native;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GestureSign.Daemon.Triggers
{
    class PinchZoomInjector
    {
        private bool _active;
        private POINT _center;
        private POINT _lastContact0;
        private POINT _lastContact1;
        private double _currentOffset;
        private const double InitialOffset = 500.0;
        private const double MinOffset = 10.0;
        private const int ContactId0 = 0;
        private const int ContactId1 = 1;
        private static bool _initialized;

        public bool IsActive => _active;

        public void Start(Point center)
        {
            EnsureInitialized();
            _center = new POINT { X = center.X, Y = center.Y };
            _currentOffset = InitialOffset;
            _active = true;

            var contacts = CreateContactPair(POINTER_FLAGS.DOWN | POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT);
            bool result = NativeMethods.InjectTouchInput(2, contacts);
            if (!result)
            {
                Logging.LogDebug($"[PinchZoom] Start failed at ({_center.X},{_center.Y}) offset={_currentOffset} error={Marshal.GetLastWin32Error()}");
                _active = false;
            }
        }

        public void Update(double distDelta, double zoomSpeed, int panDeltaX = 0, int panDeltaY = 0)
        {
            if (!_active) return;

            double effectiveSpeed = zoomSpeed <= 0 ? 1.0 : zoomSpeed;
            _currentOffset += distDelta * effectiveSpeed;
            if (_currentOffset < MinOffset)
                _currentOffset = MinOffset;

            _center.X += panDeltaX;
            _center.Y += panDeltaY;

            // 钳制 offset 确保两个触点都在屏幕虚拟桌面范围内
            ClampOffset();

            var contacts = CreateContactPair(POINTER_FLAGS.UPDATE | POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT);
            bool result = NativeMethods.InjectTouchInput(2, contacts);
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();
                Logging.LogDebug($"[PinchZoom] Update failed: offset={_currentOffset:F1} error={error}");
                // 注入失败后 Windows 取消所有活动触点，标记为非活跃
                _active = false;
            }
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

        private void ClampOffset()
        {
            var bounds = SystemInformation.VirtualScreen;
            int margin = 1;

            // 钳制中心点 Y 到屏幕范围内（触点在 center.Y，不能越界）
            int minY = bounds.Top + margin;
            int maxY = bounds.Bottom - margin;
            if (_center.Y < minY) _center.Y = minY;
            if (_center.Y > maxY) _center.Y = maxY;

            // 钳制中心点 X 到屏幕范围内（至少要能容纳 MinOffset）
            int minX = bounds.Left + margin + (int)MinOffset;
            int maxX = bounds.Right - margin - (int)MinOffset;
            if (_center.X < minX) _center.X = minX;
            if (_center.X > maxX) _center.X = maxX;

            // 确保左触点 (center.X - offset) >= bounds.Left + margin
            double maxOffsetLeft = _center.X - bounds.Left - margin;
            // 确保右触点 (center.X + offset) <= bounds.Right - margin
            double maxOffsetRight = bounds.Right - _center.X - margin;

            double maxOffset = Math.Min(maxOffsetLeft, maxOffsetRight);
            if (maxOffset < MinOffset)
                maxOffset = MinOffset;

            if (_currentOffset > maxOffset)
                _currentOffset = maxOffset;
        }

        private POINTER_TOUCH_INFO[] CreateContactPair(POINTER_FLAGS flags)
        {
            int offsetX = (int)_currentOffset;

            _lastContact0 = new POINT { X = _center.X - offsetX, Y = _center.Y };
            _lastContact1 = new POINT { X = _center.X + offsetX, Y = _center.Y };

            return new[]
            {
                CreateContact(ContactId0, _lastContact0.X, _lastContact0.Y, flags),
                CreateContact(ContactId1, _lastContact1.X, _lastContact1.Y, flags),
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
            };
        }
    }
}
