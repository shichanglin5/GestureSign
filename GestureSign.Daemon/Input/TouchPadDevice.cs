using GestureSign.Common.Input;
using GestureSign.Daemon.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GestureSign.Daemon.Input
{
    public class TouchPadDevice : HidDevice
    {
        public override Devices DeviceType => Devices.TouchPad;

        public TouchPadDevice(IntPtr rawInputBuffer, ref RAWINPUT raw) : base(rawInputBuffer, ref raw)
        {
        }

        protected override Point GetCoordinate(short linkCollection, Screen currentScr, IntPtr pRawDataPacket)
        {
            int physicalX = 0;
            int physicalY = 0;

            HidNativeApi.HidP_GetScaledUsageValue(HidReportType.Input, NativeMethods.GenericDesktopPage, linkCollection, NativeMethods.XCoordinateId, ref physicalX, _hPreparsedData.DangerousGetHandle(), pRawDataPacket, _dwSizHid);
            HidNativeApi.HidP_GetScaledUsageValue(HidReportType.Input, NativeMethods.GenericDesktopPage, linkCollection, NativeMethods.YCoordinateId, ref physicalY, _hPreparsedData.DangerousGetHandle(), pRawDataPacket, _dwSizHid);

            int x, y;
            x = physicalX * currentScr.Bounds.Width / _physicalMax.X;
            y = physicalY * currentScr.Bounds.Height / _physicalMax.Y;

            return new Point(x + currentScr.Bounds.X, y + currentScr.Bounds.Y);
        }

        public void GetRawDatas(short[] fingerIndices, Screen currentScr, ref int requiringContactCount, ref List<RawData> _outputTouchs)
        {
            // 遍历所有 HID 数据包（_dwCount 从 Windows WM_INPUT 消息获取）
            for (int dwIndex = 0; dwIndex < _dwCount; dwIndex++)
            {
                IntPtr pRawDataPacket = new IntPtr(_pRawData.ToInt64() + dwIndex * _dwSizHid);

                // 只遍历 Finger 集合（Usage=0x22）的 LinkCollection 节点
                // 非 Finger 子节点（如 Device Certification 等）不包含触点数据
                for (int fi = 0; fi < fingerIndices.Length; fi++)
                {
                    short nodeIndex = fingerIndices[fi];
                    int contactIdentifier = GetContactId(nodeIndex, pRawDataPacket);
                    Point point = GetCoordinate(nodeIndex, currentScr, pRawDataPacket);

                    ushort[] usageList = GetButtonList(_hPreparsedData.DangerousGetHandle(), pRawDataPacket, nodeIndex, _dwSizHid);
                    bool tip = usageList.Contains(NativeMethods.TipId);
                    bool primaryButton = HasPrimaryButtonPressed(_hPreparsedData.DangerousGetHandle(), pRawDataPacket, _dwSizHid);

                    var state = DeviceStates.None;
                    if (tip)
                        state |= DeviceStates.Tip;
                    if (primaryButton)
                        state |= DeviceStates.PrimaryButton;

                    _outputTouchs.Add(new RawData(state, contactIdentifier, point));

                    if (--requiringContactCount == 0) break;
                }
                if (requiringContactCount == 0) break;
            }
        }
    }
}
