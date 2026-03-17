# 触摸屏拦截与 MultiFingerDelay 机制

> 更新日期: 2026-03-15

## 1. 触摸屏输入架构

触摸屏的物理触摸产生 HID 硬件报告，Windows 对同一份数据有两个消费层次：

```text
物理触摸 → HID 硬件报告
              ├→ Windows 触摸子系统消费 → 生成 WM_POINTER → 目标窗口
              └→ RIDEV_INPUTSINK 复制一份 → GestureSign (手势识别)
```

- **上层（WM_POINTER）**：目标窗口依赖这些消息响应触摸操作（点击、滑动、拖拽等）
- **底层（HID Raw Input 副本）**：GestureSign 通过 `RIDEV_INPUTSINK` 获取 HID 报告的副本做手势识别，不影响上层消息分发

两个层次**不是两条独立数据源**，而是同一份硬件数据的两个消费者。

### 与触摸板的区别

触摸板的 HID 报告由 Precision Touchpad 驱动消费后转换为**鼠标事件**，不产生 WM_POINTER。目标窗口收到的是鼠标事件，不是触摸事件。因此触摸板不存在"拦截触摸"的需求——GestureSign 读取 HID 副本做手势识别，与鼠标事件互不干扰。

## 2. 为什么需要拦截（BlockWindowsGestures）

默认情况下两个消费层次互不干扰：GestureSign 识别手势并执行命令，目标窗口同时收到 WM_POINTER 正常响应触摸。

**问题**：当用户做多指手势时，两边同时生效——GestureSign 执行了手势命令，目标窗口也收到了多个触摸事件（被解读为多次点击、缩放等）。

开启"阻断 Windows 手势"（`AppConfig.BlockWindowsGestures`）后，`PointerInputTargetWindow` 通过 `RegisterPointerInputTarget`（需要 UIAccess 权限）劫持 WM_POINTER 消息流，作为中间人决定：

- **单指触摸**（`touchInfos.Length < threshold`）→ 通过 `InjectTouchInput` 转发给目标窗口（放行）
- **多指触摸**（`touchInfos.Length >= threshold`）→ 吞掉，不转发（GestureSign 已通过底层副本识别并处理）

单指必须放行，否则触摸屏的正常点击、滑动、拖拽全部失效。

## 3. 幽灵触摸问题

两根手指不可能在同一帧落下。当用户做二指手势时：

1. **帧1**：第一根手指 DOWN → 当前帧只有 1 个触点 < threshold(2) → 判定为单指，**注入 DOWN 给目标窗口**
2. **帧2**：第二根手指 DOWN → 当前帧 2 个触点 >= threshold(2) → 判定为多指手势，**拦截**
3. 第一根手指被标记为 BLOCKED（`_blockedPointerIds`），后续 UPDATE/UP 全部 `continue` 跳过，不注入
4. **结果**：目标窗口收到了 DOWN 但永远收不到 UP → 幽灵触摸（长按误触发、UI 卡在 pressed 状态）

### 当前代码逻辑

`PointerInputTargetWindow.GenerateInput()` 中（第 252-262 行）：

```csharp
// shouldInject 从 true 变为 false 时，把所有已映射手指标记为 BLOCKED
if (!shouldInject && _pointerIdList.Count > 0)
{
    foreach (var kvp in _pointerIdList)
    {
        if (!_blockedPointerIds.Contains(kvp.Key))
            _blockedPointerIds.Add(kvp.Key);
    }
}
```

BLOCKED 后：
- UPDATE 事件 → `continue`（第 285 行），不注入
- UP 事件 → `continue`（第 298-302 行），从映射表移除、回收 ID，但不注入

**没有为已注入 DOWN 的手指补发 UP 或 CANCELED 事件。**

## 4. MultiFingerDelay：当前的解决方案

第一根手指 DOWN 时不立即注入，缓存到 `_pendingDownEvents`，启动定时器等待（默认 100ms）：

- 等待期间第二根手指到来 → `CancelPendingInjection()`，缓存的 DOWN 从未注入，无幽灵触摸
- 等待期间手指移动超阈值或抬起 → `FlushPendingInjection()`，确认单指操作，补注入缓存
- 定时器超时 → 确认单指，正常注入

**代价**：所有单指操作有最多 100ms 额外延迟。

### 当前配置状态

- `AppConfig.MultiFingerDelay`：属性保留（默认 100ms），`PointerInputTargetWindow` 运行时使用
- Options UI：在 BlockWindowsGestures 开关下方显示 Slider（0-200ms，步长 10），仅在开启阻断时可见
- 仅在触摸屏 + 开启阻断 Windows 手势（`threshold >= 2`）时生效
- 设为 0 时完全依赖 CANCELED|UP 补发机制

## 5. Windows 原生的处理方式

### 延迟等待（预判）

Windows 自身在多个层面对触摸输入做了延迟等待：

- **Press-and-hold 手势**：默认启用，Windows 会 hold 住第一次触摸，等判断是否是长按右键手势后才派发 `WM_TOUCHDOWN`。可通过 `WM_TABLET_QUERYSYSTEMGESTURESTATUS` 禁用
- **三指/四指手势检测**：Windows 11 开启三四指手势时，系统会引入短暂延迟来判断是否有更多手指落下
- **Palm rejection**：触摸板和触摸屏上，掌缘误触检测会 hold 住触点数据若干帧，等确认是有效触摸后才接受

### 补发 CANCELED（后悔）

当触摸事件已经派发给应用后，Windows 的系统手势识别器（DirectManipulation）仍然可以"反悔"：

- 识别到多指手势时，给应用补发 `POINTER_FLAG_CANCELED` + `WM_POINTERCAPTURECHANGED`
- 应用收到 `CANCELED` 后应撤销该触点的交互（不触发 tap/click）
- 收到 `WM_POINTERCAPTURECHANGED` 后不再期望该 pointer 的后续消息

Windows 的策略是：**能提前判断就延迟等待，来不及就事后取消**。

### 参考文档

- [InjectTouchInput - POINTER_FLAG_CANCELED 用法](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-injecttouchinput)
- [WM_POINTERUP - POINTER_FLAG_CANCELED 说明](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointerup)
- [Pointer Flags 常量定义](https://learn.microsoft.com/en-us/windows/win32/inputmsg/pointer-flags-contants)
- [WM_POINTERCAPTURECHANGED - 手势接管机制](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointercapturechanged)
- [触摸交互设计指南 - "交互不应基于时间区分"](https://learn.microsoft.com/en-us/windows/apps/design/input/touch-interactions)

## 6. 优化方案

### 方案 a：补发 CANCELED | UP ✅ 已实施

当第二帧发现需要拦截时（`shouldInject` 从 true 变为 false），在 `GenerateInput()` 中标记 BLOCKED 之前，为所有已注入过 DOWN 的手指补发 `POINTER_FLAGS.CANCELED | POINTER_FLAGS.UP` 事件。

**实现要点**：
- 新增 `_injectedFingerPositions` 字典（`injectedId → lastPosition`），在 DOWN/UPDATE 注入时记录位置，UP 注入时移除
- 标记 BLOCKED 时，检查 `_injectedFingerPositions` 中是否有该手指的记录，有则生成合成 `CANCELED | UP` 事件
- 合成事件的 `PtPixelLocation` 使用最后记录的位置，避免 `InjectTouchInput` 返回 `ERROR_INVALID_PARAMETER`
- 全部手指 UP 时清理 `_injectedFingerPositions`

**优势**：
- 与 Windows 原生 DirectManipulation 的 CANCELED 机制一致
- `CANCELED` 标志告诉目标应用撤销交互，不触发 tap/click
- 与 MultiFingerDelay 配合使用时提供双重保障（方案 b）

### 方案 b：缩短延迟 + CANCELED 兜底（当前默认）

MultiFingerDelay 默认 100ms 延迟覆盖大多数情况。对于延迟窗口外漏网的情况（如两根手指间隔 > 延迟时间），方案 a 的 CANCELED 补发作为兜底。用户可通过 Options UI 调整延迟值（0-200ms），设为 0 则完全依赖 CANCELED 补发。

### 方案 c：仅延迟（旧方案，已被方案 b 替代）

仅延迟，无 CANCELED 兜底。100ms 延迟期内如果第二根手指未到达，第一根手指的 DOWN 会被注入，之后拦截时无法取消已注入的 DOWN。

## 7. 相关文件

- `GestureSign.Daemon/Filtration/PointerInputTargetWindow.cs` — 触摸拦截/注入核心逻辑
- `GestureSign.Daemon/Input/PointCapture.cs` — `BlockTouchInputThreshold` 设置与 `_pointerInputTargetWindow` 管理
- `GestureSign.Common/Configuration/AppConfig.cs` — `MultiFingerDelay`、`BlockWindowsGestures` 配置
