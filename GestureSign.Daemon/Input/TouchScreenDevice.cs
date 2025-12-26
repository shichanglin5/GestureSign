using GestureSign.Common.Input;
using GestureSign.Daemon.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GestureSign.Daemon.Input
{
    public class TouchScreenDevice : HidDevice
    {
        public override Devices DeviceType => Devices.TouchScreen;

        public TouchScreenDevice(IntPtr rawInputBuffer, ref RAWINPUT raw) : base(rawInputBuffer, ref raw)
        {
        }

        public void GetRawDatas(short numberOfChildren, Screen currentScr, ref int requiringContactCount, ref List<RawData> _outputTouchs)
        {
            // 遍历所有 HID 数据包（_dwCount 从 Windows WM_INPUT 消息获取）
            for (int dwIndex = 0; dwIndex < _dwCount; dwIndex++)
            {
                IntPtr pRawDataPacket = new IntPtr(_pRawData.ToInt64() + dwIndex * _dwSizHid);

                // 遍历当前数据包的所有节点（每个节点对应一个触点）
                for (short nodeIndex = 1; nodeIndex <= numberOfChildren; nodeIndex++)
                {
                    int contactIdentifier = GetContactId(nodeIndex, pRawDataPacket);
                    Point point = GetCoordinate(nodeIndex, currentScr, pRawDataPacket);

                    ushort[] usageList = GetButtonList(_hPreparsedData.DangerousGetHandle(), _pRawData, nodeIndex, _dwSizHid);
                    bool tip = usageList.Length != 0 && usageList[0] == NativeMethods.TipId;

                    // 添加触点数据
                    _outputTouchs.Add(new RawData(tip ? DeviceStates.Tip : DeviceStates.None, contactIdentifier, point));

                    // requiringContactCount 倒计时：每收集一个触点就递减
                    // 当减到 0 时，说明收集到了预期数量的触点，提前结束循环
                    if (--requiringContactCount == 0) break;
                }
                if (requiringContactCount == 0) break;
            }

            // 循环结束后：
            //   - requiringContactCount = 0: 数据完整，收集到了所有预期的触点
            //   - requiringContactCount > 0: 数据不完整，HID 数据包中的触点少于 HID 报告声称的数量
            //     （这种情况在手指抬起时很常见）
        }
    }
}
