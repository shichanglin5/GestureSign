# 修复 HID 多指触点解析不完整的问题

## 问题描述

Surface Flex Keyboard 触摸板上，3 指及以上手势仅能采集到 2 个有效触点坐标，剩余触点数据为空或无效。导致多指手势识别异常。

## 根因分析

### 1. Finger 节点遍历错误

HID 触摸设备的 LinkCollectionNodes 不一定全是 Finger 集合。例如 Surface Flex Keyboard 有 4 个子节点：

```
[0] Root
[1] Finger (Usage=0x22, UsagePage=0x0D)
[2] Finger (Usage=0x22, UsagePage=0x0D)
[3] Device Certification Status (非 Finger)
[4] Latency Mode (非 Finger)
```

旧代码 `for (nodeIndex = 1; nodeIndex <= numberOfChildren; nodeIndex++)` 直接用 `numberOfChildren` 遍历所有子节点，导致对非 Finger 节点（索引 3、4）调用 `GetContactId`/`GetCoordinate` 返回空数据，被误认为有效触点。

### 2. Hybrid 模式不支持

该设备只有 2 个 Finger 槽位（`fingerSlots=2`），但最多支持 5 个同时触点。设备使用 HID **Hybrid** 报告模式：

- 第 1 个 HID 报告：`contactCount=5`（声明总触点数），包含 2 个 Finger 数据
- 第 2 个 HID 报告：`contactCount=0`（续传标记），包含 2 个 Finger 数据
- 第 3 个 HID 报告：`contactCount=0`（续传标记），包含 1 个 Finger 数据

旧代码将 `contactCount=0` 一律视为"无触点/手势结束"，无法拼合跨报告的触点数据。

### 3. GetButtonList 传参错误

`GetButtonList` 的第二个参数传的是 `_pRawData`（原始缓冲区起始地址），应传 `pRawDataPacket`（当前遍历的数据包地址）。当 `_dwCount > 1` 时会读错数据。

## 修复方案

### 修改文件

| 文件 | 修改内容 |
|------|---------|
| `HidDevice.cs` | 新增 `FingerUsageId` 常量和 `GetFingerLinkCollectionIndices()` 方法 |
| `TouchPadDevice.cs` | `GetRawDatas` 改用 `fingerIndices` 遍历 + 修复 `GetButtonList` 传参 |
| `TouchScreenDevice.cs` | 同上 |
| `MessageWindow.cs` | 新增 `_hybridPending` 状态机 + Hybrid 续传逻辑 + 诊断日志 |

### 核心改动

#### 1. 只遍历 Finger 节点

```csharp
// HidDevice.cs - 新增方法
public static short[] GetFingerLinkCollectionIndices(
    HIDP_LINK_COLLECTION_NODE[] linkCollection)
{
    // 只筛选 Usage=0x22 (Finger), UsagePage=0x0D (Digitizer) 的节点
    // 跳过 Device Certification、Latency Mode 等非 Finger 节点
}
```

`GetRawDatas` 参数从 `short numberOfChildren` 改为 `short[] fingerIndices`，只遍历真正的 Finger 集合。

#### 2. Hybrid 续传状态机

```
                     contactCount != 0
                    ┌──────────────────────────────────┐
                    │  首包：收集 fingerSlots 个触点     │
                    │  remaining = contactCount - slots  │
                    │  _hybridPending = remaining > 0    │
                    │    && contactCount > slots          │
                    └──────────┬───────────────────────────┘
                               │ _hybridPending == true
                               ▼
                     contactCount == 0 && _hybridPending
                    ┌──────────────────────────────────┐
                    │  续传包：继续收集触点              │
                    │  _hybridPending = remaining > 0   │
                    └──────────┬───────────────────────────┘
                               │ _hybridPending == false (remaining == 0)
                               ▼
                    ┌──────────────────────────────────┐
                    │  PointsIntercepted: 上送完整数据   │
                    └──────────────────────────────────┘
```

关键设计决策：
- `contactCount=0` 时，**只有** `_hybridPending == true` 才走续传路径
- 非 Hybrid 的 `_requiringContactCount > 0`（抬手阶段数据不完整）保持原逻辑：照常上送让上层收敛
- `UpdateRegistration()` 中显式重置 `_hybridPending = false`，避免设备更新/休眠恢复后状态残留

#### 3. 修复 GetButtonList 传参

```csharp
// 旧：GetButtonList(..., _pRawData, ...)   ← 缓冲区起始地址（错误）
// 新：GetButtonList(..., pRawDataPacket, ...) ← 当前数据包地址（正确）
```

## 验证数据

### 5 指 Hybrid 拼包（Surface Flex Keyboard TouchPad, fingerSlots=2）

```
Hybrid START: contactCount=5, fingerSlots=2, remaining=3, collected=2
Hybrid CONT:  collected=2->4, remaining=1, pending=True
Hybrid CONT:  collected=4->5, remaining=0, pending=False
PointsIntercepted: count=5, remaining=0, device=TouchPad
```

- 3 个 HID 报告拼出完整 5 触点：2+2+1=5
- 模式在密集连续输入下完全稳定（数百帧零异常）

### 3 指 Hybrid 拼包

```
Hybrid START: contactCount=3, fingerSlots=2, remaining=1, collected=2
Hybrid CONT:  collected=2->3, remaining=0, pending=False
PointsIntercepted: count=3, remaining=0, device=TouchPad
```

### 抬手收敛序列

```
PointsIntercepted: count=3, remaining=0  → 3 指
PointsIntercepted: count=2, remaining=0  → 抬起 1 指
PointsIntercepted: count=1, remaining=0  → 抬起 1 指
All fingers up, resetting sourceDevice    → 干净重置
```

抬手阶段不进入 Hybrid 路径，正常递减上送，`sourceDevice` 在所有手指抬起后正确重置。

## 已知限制

- 本次修复解决的是输入层"坐标采集完整性"问题
- TipTap 等高级手势识别还涉及系统触摸拦截、时序判定和上层识别器，需要独立验证
- Hybrid 路径当前的诊断日志为 DEBUG 级别，可在确认稳定后降级或移除
