using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Daemon.Native;

namespace GestureSign.Daemon.Input
{
    public class MessageWindow : NativeWindow
    {
        private Screen _currentScr;

        private static readonly HandleRef HwndMessage = new HandleRef(null, new IntPtr(-3));

        // _outputTouchs: 从 HID 数据包中实际收集到的触点数据
        private List<RawData> _outputTouchs = new List<RawData>(1);

        // _requiringContactCount: 期望但尚未收集到的触点数（倒计时）
        //   - 初始值 = contactCount（HID 报告声称的触点数）
        //   - GetRawDatas 每收集一个触点就递减
        //   - 结束时如果 > 0，说明 HID 数据不完整
        private int _requiringContactCount;

        private Dictionary<IntPtr, ushort> _validDevices = new Dictionary<IntPtr, ushort>();

        private Devices _sourceDevice;
        private List<ushort> _registeredDeviceList = new List<ushort>(1);

        public event RawPointsDataMessageEventHandler PointsIntercepted;

        public MessageWindow()
        {
            CreateWindow();
            UpdateRegistration();
        }

        ~MessageWindow()
        {
            DestroyWindow();
        }

        public bool CreateWindow()
        {
            if (Handle == IntPtr.Zero)
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                CreateHandle(new CreateParams
                {
                    Style = 0,
                    ExStyle = WS_EX_NOACTIVATE,
                    ClassStyle = 0,
                    Caption = "GSMessageWindow",
                    Parent = (IntPtr)HwndMessage
                });
            }
            return Handle != IntPtr.Zero;
        }

        public void DestroyWindow()
        {
            DestroyWindow(true, IntPtr.Zero);
        }

        public override void DestroyHandle()
        {
            DestroyWindow(false, IntPtr.Zero);
            base.DestroyHandle();
        }

        protected override void OnHandleChange()
        {
            UpdateRegistration();
            base.OnHandleChange();
        }

        private bool GetInvokeRequired(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            int pid;
            var hwndThread = NativeMethods.GetWindowThreadProcessId(new HandleRef(this, hWnd), out pid);
            var currentThread = NativeMethods.GetCurrentThreadId();
            return (hwndThread != currentThread);
        }

        private void DestroyWindow(bool destroyHwnd, IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                hWnd = Handle;
            }

            if (GetInvokeRequired(hWnd))
            {
                NativeMethods.PostMessage(new HandleRef(this, hWnd), NativeMethods.WmClose, 0, 0);
                return;
            }

            lock (this)
            {
                if (destroyHwnd)
                {
                    base.DestroyHandle();
                }
            }
        }

        public void UpdateRegistration()
        {
            GestureSign.Common.Log.Logging.LogInfo($"[MessageWindow] UpdateRegistration called, clearing {_validDevices.Count} cached devices, resetting sourceDevice={_sourceDevice}");
            _validDevices.Clear();
            _sourceDevice = Devices.None;
            _requiringContactCount = 0;
            _outputTouchs.Clear();

            // GestureSign.Common.Log.Logging.LogInfo($"[MessageWindow] Registering devices - TouchScreen: {AppConfig.RegisterTouchScreen}, TouchPad: {AppConfig.RegisterTouchPad}");
            UpdateRegisterState(AppConfig.RegisterTouchScreen, NativeMethods.TouchScreenUsage);
            UpdateRegisterState(AppConfig.RegisterTouchPad, NativeMethods.TouchPadUsage);
            GestureSign.Common.Log.Logging.LogInfo($"[MessageWindow] UpdateRegistration completed, {_registeredDeviceList.Count} devices registered");
        }

        private void UpdateRegisterState(bool register, ushort usage)
        {
            string deviceName = usage == NativeMethods.TouchScreenUsage ? "TouchScreen" :
                               usage == NativeMethods.TouchPadUsage ? "TouchPad" : $"Unknown(0x{usage:X})";

            if (register)
            {
                // GestureSign.Common.Log.Logging.LogInfo($"[MessageWindow] Registering {deviceName} (usage: 0x{usage:X})");
                RegisterDevice(usage);
            }
            else
            {
                GestureSign.Common.Log.Logging.LogInfo($"[MessageWindow] Unregistering {deviceName} (usage: 0x{usage:X})");
                UnregisterDevice(usage);
            }
        }

        private void RegisterDevice(ushort usage)
        {
            string deviceName = usage == NativeMethods.TouchScreenUsage ? "TouchScreen" :
                               usage == NativeMethods.TouchPadUsage ? "TouchPad" : $"Unknown(0x{usage:X})";

            UnregisterDevice(usage);

            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];

            rid[0].usUsagePage = NativeMethods.DigitizerUsagePage;
            rid[0].usUsage = usage;
            rid[0].dwFlags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_DEVNOTIFY;
            rid[0].hwndTarget = Handle;

            GestureSign.Common.Log.Logging.LogDebug($"[MessageWindow] Calling RegisterRawInputDevices for {deviceName} (hwnd: 0x{Handle:X})");

            if (!NativeMethods.RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(rid[0])))
            {
                int error = Marshal.GetLastWin32Error();
                GestureSign.Common.Log.Logging.LogError($"[MessageWindow] Failed to register {deviceName}: Win32Error={error}");
                throw new ApplicationException($"Failed to register raw input device {deviceName} (error: {error})");
            }
            _registeredDeviceList.Add(usage);
            GestureSign.Common.Log.Logging.LogInfo($"[MessageWindow] Successfully registered {deviceName}");
        }

        private void UnregisterDevice(ushort usage)
        {
            if (_registeredDeviceList.Contains(usage))
            {
                string deviceName = usage == NativeMethods.TouchScreenUsage ? "TouchScreen" :
                                   usage == NativeMethods.TouchPadUsage ? "TouchPad" : $"Unknown(0x{usage:X})";

                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];

                rid[0].usUsagePage = NativeMethods.DigitizerUsagePage;
                rid[0].usUsage = usage;
                rid[0].dwFlags = NativeMethods.RIDEV_REMOVE;
                rid[0].hwndTarget = IntPtr.Zero;

                GestureSign.Common.Log.Logging.LogDebug($"[MessageWindow] Unregistering {deviceName}");

                if (!NativeMethods.RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(rid[0])))
                {
                    int error = Marshal.GetLastWin32Error();
                    GestureSign.Common.Log.Logging.LogWarning($"[MessageWindow] Failed to unregister {deviceName}: Win32Error={error}");
                    throw new ApplicationException($"Failed to unregister raw input device {deviceName} (error: {error})");
                }
                _registeredDeviceList.Remove(usage);
                GestureSign.Common.Log.Logging.LogDebug($"[MessageWindow] Successfully unregistered {deviceName}");
            }
        }

        private bool ValidateDevice(IntPtr hDevice, out ushort usage)
        {
            usage = 0;
            uint pcbSize = 0;
            NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICEINFO, IntPtr.Zero, ref pcbSize);
            if (pcbSize <= 0)
                return false;

            IntPtr pInfo = Marshal.AllocHGlobal((int)pcbSize);
            using (new SafeUnmanagedMemoryHandle(pInfo))
            {
                NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICEINFO, pInfo, ref pcbSize);
                var info = (RID_DEVICE_INFO)Marshal.PtrToStructure(pInfo, typeof(RID_DEVICE_INFO));
                switch (info.hid.usUsage)
                {
                    case NativeMethods.TouchPadUsage:
                    case NativeMethods.TouchScreenUsage:
                    case NativeMethods.PenUsage:
                        break;
                    default:
                        return true;
                }

                NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref pcbSize);
                if (pcbSize <= 0)
                    return false;

                IntPtr pData = Marshal.AllocHGlobal((int)pcbSize);
                using (new SafeUnmanagedMemoryHandle(pData))
                {
                    NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, pData, ref pcbSize);
                    string deviceName = Marshal.PtrToStringAnsi(pData);

                    if (string.IsNullOrEmpty(deviceName) || deviceName.IndexOf("VIRTUAL_DIGITIZER", StringComparison.OrdinalIgnoreCase) >= 0 || deviceName.IndexOf("ROOT", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    usage = info.hid.usUsage;
                    return true;
                }
            }
        }

        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case NativeMethods.WM_INPUT:
                    {
                        ProcessInputCommand(message.LParam);
                        break;
                    }
                case NativeMethods.WM_INPUT_DEVICE_CHANGE:
                    {
                        // wParam indicates GIDC_ARRIVAL (1) or GIDC_REMOVAL (2)
                        string changeType = message.WParam.ToInt32() == 1 ? "ARRIVAL" : "REMOVAL";
                        GestureSign.Common.Log.Logging.LogInfo($"[MessageWindow] WM_INPUT_DEVICE_CHANGE: {changeType}, lParam=0x{message.LParam:X}");
                        _validDevices.Clear();
                        break;
                    }
            }
            base.WndProc(ref message);
        }

        private void CheckLastError()
        {
            int errCode = Marshal.GetLastWin32Error();
            if (errCode != 0)
            {
                throw new Win32Exception(errCode);
            }
        }

        #region ProcessInput

        /// <summary>
        /// Processes WM_INPUT messages to retrieve information about any
        /// touch events that occur.
        /// </summary>
        /// <param name="LParam">The WM_INPUT message to process.</param>
        private void ProcessInputCommand(IntPtr LParam)
        {
            uint dwSize = 0;

            // First call to GetRawInputData sets the value of dwSize
            // dwSize can then be used to allocate the appropriate amount of memore,
            // storing the pointer in "buffer".
            NativeMethods.GetRawInputData(LParam, NativeMethods.RID_INPUT, IntPtr.Zero,
                             ref dwSize,
                             (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));

            IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
            try
            {
                // Check that buffer points to something, and if so,
                // call GetRawInputData again to fill the allocated memory
                // with information about the input
                if (buffer == IntPtr.Zero ||
                   NativeMethods.GetRawInputData(LParam, NativeMethods.RID_INPUT,
                                     buffer,
                                     ref dwSize,
                                     (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) != dwSize)
                {
                    throw new ApplicationException("GetRawInputData does not return correct size !\n.");
                }

                RAWINPUT raw = (RAWINPUT)Marshal.PtrToStructure(buffer, typeof(RAWINPUT));

                ushort usage;
                if (!_validDevices.TryGetValue(raw.header.hDevice, out usage))
                {
                    if (ValidateDevice(raw.header.hDevice, out usage))
                        _validDevices.Add(raw.header.hDevice, usage);
                }

                if (usage == 0)
                    return;
                if (usage == NativeMethods.TouchScreenUsage)
                {
                    if (_sourceDevice == Devices.None)
                    {
                        _currentScr = Screen.FromPoint(Cursor.Position);
                        if (_currentScr == null)
                            return;
                        _sourceDevice = Devices.TouchScreen;
                        TouchScreenDevice.GetCurrentScreenOrientation();
                    }
                    else if (_sourceDevice != Devices.TouchScreen)
                        return;

                    using (TouchScreenDevice touchScreen = new TouchScreenDevice(buffer, ref raw))
                    {
                        // contactCount: HID 驱动报告的触点数量（从 HID 报告头部解析）
                        int contactCount = touchScreen.GetContactCount();

                        HidNativeApi.HIDP_LINK_COLLECTION_NODE[] linkCollection = touchScreen.GetLinkCollectionNodes();
                        touchScreen.GetPhysicalMax(linkCollection.Length);

                        if (contactCount != 0)
                        {
                            // _requiringContactCount: 期望收集的触点数，初始值 = contactCount
                            // GetRawDatas 会递减这个值，如果最终 != 0，说明 HID 数据不完整
                            _requiringContactCount = contactCount;
                            _outputTouchs = new List<RawData>(contactCount);
                            touchScreen.GetRawDatas(linkCollection[0].NumberOfChildren, _currentScr, ref _requiringContactCount, ref _outputTouchs);

                            // 注意：GetRawDatas 结束后，_requiringContactCount 可能 > 0（数据不完整）
                            // 这种情况在手指抬起时很常见：HID 报告说有 N 个触点，但实际数据包不完整
                        }
                        else
                        {
                            // contactCount == 0：HID 报告说没有任何触点
                            // 实际测试发现，大部分触摸屏驱动在所有手指抬起后会停止发送 WM_INPUT，
                            // 而不是发送 contactCount=0 的消息，所以这个分支很少执行

                            if (_requiringContactCount == 0)
                                return; // No ongoing gesture, skip

                            // 如果之前有手势，现在 contactCount=0，说明所有手指确实抬起了
                            GestureSign.Common.Log.Logging.LogWarning($"[MessageWindow-TouchScreen] contactCount=0 with ongoing gesture, sending empty event to end gesture");
                            _requiringContactCount = 0;
                            _outputTouchs = new List<RawData>();
                        }
                    }
                }
                else if (usage == NativeMethods.TouchPadUsage)
                {
                    if (_sourceDevice == Devices.None)
                    {
                        _currentScr = Screen.FromPoint(Cursor.Position);
                        if (_currentScr == null)
                            return;
                        _sourceDevice = Devices.TouchPad;
                    }
                    else if (_sourceDevice != Devices.TouchPad)
                        return;

                    using (TouchPadDevice touchPad = new TouchPadDevice(buffer, ref raw))
                    {
                        // contactCount: HID 驱动报告的触点数量（从 HID 报告头部解析）
                        int contactCount = touchPad.GetContactCount();

                        HidNativeApi.HIDP_LINK_COLLECTION_NODE[] linkCollection = touchPad.GetLinkCollectionNodes();
                        touchPad.GetPhysicalMax(linkCollection.Length);

                        if (contactCount != 0)
                        {
                            // _requiringContactCount: 期望收集的触点数，初始值 = contactCount
                            // GetRawDatas 会递减这个值，如果最终 != 0，说明 HID 数据不完整
                            _requiringContactCount = contactCount;
                            _outputTouchs = new List<RawData>(contactCount);
                            touchPad.GetRawDatas(linkCollection[0].NumberOfChildren, _currentScr, ref _requiringContactCount, ref _outputTouchs);

                            // 注意：GetRawDatas 结束后，_requiringContactCount 可能 > 0（数据不完整）
                            // 这种情况在手指抬起时很常见：HID 报告说有 N 个触点，但实际数据包不完整
                        }
                        else
                        {
                            // contactCount == 0：HID 报告说没有任何触点
                            // 实际测试发现，大部分触摸板驱动在所有手指抬起后会停止发送 WM_INPUT，
                            // 而不是发送 contactCount=0 的消息，所以这个分支很少执行

                            if (_requiringContactCount == 0)
                                return; // No ongoing gesture, skip

                            // 如果之前有手势，现在 contactCount=0，说明所有手指确实抬起了
                            GestureSign.Common.Log.Logging.LogWarning($"[MessageWindow-TouchPad] contactCount=0 with ongoing gesture, sending empty event to end gesture");
                            _requiringContactCount = 0;
                            _outputTouchs = new List<RawData>();
                        }
                    }
                }

                if (PointsIntercepted != null)
                {
                    // ==================== 关键设计决策 ====================
                    //
                    // 为什么不检查 _requiringContactCount == 0？
                    //
                    // 问题背景：
                    //   手指抬起时，HID 数据经常不完整，导致手势卡住：
                    //   1. HID 报告说 contactCount = 4
                    //   2. 但实际数据包只包含 3 个触点
                    //   3. GetRawDatas 结束后 _requiringContactCount = 1 (还差1个)
                    //   4. 如果检查 _requiringContactCount == 0，事件会被丢弃
                    //   5. PointEventTranslator 收不到通知，手势状态卡在 Capturing
                    //   6. 最终只能靠 100ms 超时清理
                    //
                    // 解决方案：
                    //   无论数据是否完整，都发送已收集到的触点
                    //   让 PointEventTranslator 通过检测触点数量变化（4→3）来判断手指抬起
                    //
                    // 变量含义：
                    //   - _requiringContactCount: 期望但未收集到的触点数（0=完整，>0=不完整）
                    //   - _outputTouchs.Count: 实际收集到的触点数
                    //
                    // ====================================================

                    // Log touch data for debugging
                    string touchStates = string.Join(", ", _outputTouchs.Select(rd => $"{rd.ContactIdentifier}:{rd.State}"));

                    // Use _outputTouchs.Count (actual collected slots) as total finger count
                    // This includes all slots even if some have State=None
                    int totalFingerCount = _outputTouchs.Count;

                    // 发送触点数据给 PointEventTranslator
                    // 即使 _requiringContactCount > 0（数据不完整），也要发送
                    // 传递 totalFingerCount（实际收集到的触点槽位数）以保留手指总数信息
                    PointsIntercepted(this, new RawPointsDataMessageEventArgs(_outputTouchs, _sourceDevice, totalFingerCount));

                    // 重置设备状态：当所有触点的 State 都是 None 时
                    if (_outputTouchs.TrueForAll(rd => rd.State == DeviceStates.None))
                    {
                        _sourceDevice = Devices.None;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }


        #endregion ProcessInput
    }
}

