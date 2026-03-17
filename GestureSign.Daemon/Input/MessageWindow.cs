using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
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
        //   - 结束时如果 > 0，说明需要 Hybrid 续传或数据不完整
        private int _requiringContactCount;

        // _hybridPending: 当 contactCount > Finger 槽位数时为 true，表示需要等待续传报告
        private bool _hybridPending;

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
            GestureSign.Common.Log.Logging.LogDebug($"[MessageWindow] UpdateRegistration called, clearing {_validDevices.Count} cached devices, resetting sourceDevice={_sourceDevice}");
            _validDevices.Clear();
            _sourceDevice = Devices.None;
            _requiringContactCount = 0;
            _hybridPending = false;
            _outputTouchs.Clear();

            // GestureSign.Common.Log.Logging.LogInfo($"[MessageWindow] Registering devices - TouchScreen: {AppConfig.RegisterTouchScreen}, TouchPad: {AppConfig.RegisterTouchPad}");
            UpdateRegisterState(AppConfig.RegisterTouchScreen, NativeMethods.TouchScreenUsage);
            UpdateRegisterState(AppConfig.RegisterTouchPad, NativeMethods.TouchPadUsage);
            GestureSign.Common.Log.Logging.LogDebug($"[MessageWindow] UpdateRegistration completed, {_registeredDeviceList.Count} devices registered");
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
                    if (_sourceDevice == Devices.None || (_sourceDevice != Devices.TouchScreen && _requiringContactCount == 0))
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
                        int contactCount = touchScreen.GetContactCount();

                        HidNativeApi.HIDP_LINK_COLLECTION_NODE[] linkCollection = touchScreen.GetLinkCollectionNodes();
                        touchScreen.GetPhysicalMax(linkCollection.Length);

                        short[] fingerIndices = HidDevice.GetFingerLinkCollectionIndices(linkCollection);

                        if (contactCount != 0)
                        {
                            _requiringContactCount = contactCount;
                            _outputTouchs = new List<RawData>(contactCount);
                            touchScreen.GetRawDatas(fingerIndices, _currentScr, ref _requiringContactCount, ref _outputTouchs);
                            // 触点数超过 Finger 槽位数 → 需要后续 Hybrid 续传报告
                            _hybridPending = _requiringContactCount > 0 && contactCount > fingerIndices.Length;
                        }
                        else if (_hybridPending)
                        {
                            // Hybrid 续传：仅当上一帧明确标记为 Hybrid 待续时才继续拼包
                            touchScreen.GetRawDatas(fingerIndices, _currentScr, ref _requiringContactCount, ref _outputTouchs);
                            _hybridPending = _requiringContactCount > 0;
                        }
                        else if (_requiringContactCount > 0)
                        {
                            // 非 Hybrid：数据不完整（手指抬起），仍照常上送让上层收敛
                        }
                        else
                        {
                            return;
                        }
                    }
                }
                else if (usage == NativeMethods.TouchPadUsage)
                {
                    if (_sourceDevice == Devices.None || (_sourceDevice != Devices.TouchPad && _requiringContactCount == 0))
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
                        int contactCount = touchPad.GetContactCount();

                        HidNativeApi.HIDP_LINK_COLLECTION_NODE[] linkCollection = touchPad.GetLinkCollectionNodes();
                        touchPad.GetPhysicalMax(linkCollection.Length);

                        short[] fingerIndices = HidDevice.GetFingerLinkCollectionIndices(linkCollection);

                        if (contactCount != 0)
                        {
                            _requiringContactCount = contactCount;
                            _outputTouchs = new List<RawData>(contactCount);
                            touchPad.GetRawDatas(fingerIndices, _currentScr, ref _requiringContactCount, ref _outputTouchs);
                            _hybridPending = _requiringContactCount > 0 && contactCount > fingerIndices.Length;
                        }
                        else if (_hybridPending)
                        {
                            // Hybrid 续传：仅当上一帧明确标记为 Hybrid 待续时才继续拼包
                            touchPad.GetRawDatas(fingerIndices, _currentScr, ref _requiringContactCount, ref _outputTouchs);
                            _hybridPending = _requiringContactCount > 0;
                        }
                        else if (_requiringContactCount > 0)
                        {
                            // 非 Hybrid：数据不完整（手指抬起），仍照常上送让上层收敛
                        }
                        else
                        {
                            return;
                        }
                    }
                }

                if (PointsIntercepted != null)
                {
                    // Hybrid 模式：触点数超过 Finger 槽位数时，需要等续传报告收齐所有触点再发送。
                    // 非 Hybrid 情况下 _requiringContactCount > 0 表示数据不完整（手指抬起），仍需发送。
                    if (_hybridPending)
                        return;

                    PointsIntercepted(this, new RawPointsDataMessageEventArgs(_outputTouchs, _sourceDevice, _outputTouchs.Count));
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

