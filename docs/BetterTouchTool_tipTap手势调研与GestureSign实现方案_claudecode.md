# BetterTouchTool TipTap 手势调研与 GestureSign 实现分析

> **调研日期**: 2026-03-10
> **调研工具**: Claude Code

---

## 1. BetterTouchTool TipTap 手势功能概述

### 1.1 什么是 TipTap

TipTap 是 BetterTouchTool (BTT) 为 macOS 触控板和 Magic Mouse 提供的手势类型。核心概念：**一个或多个手指保持静止（"Tip"），另一个手指在其左侧/右侧/中间位置快速轻点（"Tap"）**。

用户操作流程：
```
T0: 固定手指触摸触控面 → 保持静止（Tip）
T1: 另一根手指在固定手指的左/右/中间位置快速点一下（Tap）
T2: 轻点手指抬起 → 手势触发
T3: (可选) 固定手指不动，重复轻点 → 连续触发
Tend: 所有手指离开触控面
```

### 1.2 手势变体（完整列表）

按 **固定手指数量 × 轻点方向** 组合，共 9 种变体：

| Fix 级别 | 总手指数 | Left | Right | Middle |
|----------|---------|------|-------|--------|
| 1 Finger Fix | 2 | ✅ | ✅ | ✅ |
| 2 Finger Fix | 3 | ✅ | ✅ | ✅ |
| 3 Finger Fix | 4 | ✅ | ✅ | ✅ |

### 1.3 手指角色

- **Tip（固定手指）**：先触摸并保持静止，整个手势期间不移动。定义为"触摸后保持不动"（类似 mouse down）
- **Tap（轻点手指）**：在固定手指之后触摸，短暂触碰后立即抬起。定义为"短暂触摸后几乎立刻移开"（类似 mouse click）

### 1.4 时序机制

BTT 没有公开精确时序参数，从用户报告和行为可推断：

**固定手指判定**：
- 必须先于轻点手指触摸触控面
- 位移低于静止阈值
- 一旦移动/滑动，手势不触发

**轻点判定**：
- touch-down 到 touch-up 时间足够短（类似普通 tap 窗口）
- 部分配置下需要 "Tip-Tap-Tap"（双击）才触发，暗示存在确认机制

**重复间隔**：
- 1 Finger Fix 重复响应很快，无明显延迟
- 2/3 Finger Fix 快速重复需要约 0.3-0.4 秒间隔（与多手指静止重确认有关）

**灵敏度**：BTT 在 Advanced > Trackpad > TipTaps 中提供灵敏度调节。

### 1.5 BTT 如何区分 TipTap 与其他手势

**TipTap vs 两指滚动（核心难题）**：

这是 BTT 面临的最大挑战。两指滚动和 1 Finger Fix TipTap 都涉及两根手指。区分判据（推断）：
1. **时间差**：固定手指必须先于轻点手指明显时间到达
2. **固定手指位移**：整个过程中位移接近零（排除滚动/滑动）
3. **轻点手指生命周期**：touch-down 到 touch-up 时间很短
4. **相对位置**：轻点手指与固定手指的水平相对位置决定 Left/Right/Middle

**TipTap vs 多指 Tap**：
- 普通两指 Tap：两指几乎同时触碰和抬起
- TipTap：固定手指保持不动，只有轻点手指进行 touch-up
- 关键区别在于手指的时间异步性和固定手指的持续性

### 1.6 典型应用场景

- 切换浏览器标签：TipTap Right/Left → Ctrl+Tab / Ctrl+Shift+Tab
- 关闭标签：TipTap Middle → Ctrl+W
- 浏览器前进/后退
- 中键点击模拟
- CAD 应用中键操作
- 支持 Action Repeat（动作重复）和 On Touch Release（触摸释放触发）

### 1.7 BTT 的已知问题

| 问题 | 描述 |
|------|------|
| 两指滚动误触发 | 两指滚动被误识别为 TipTap（最常见投诉，近十年未完全解决）|
| 方向误判 | 快速交替 TipTap 时方向可能"卡住" |
| 多指重复延迟 | 2/3 Finger Fix 快速重复需要 0.3-0.4s 间隔 |
| 双击才触发 | 部分配置下需要 Tip-Tap-Tap 才能工作 |
| macOS 版本兼容性 | Monterey 等版本更新后约 20% 识别失败率 |
| Middle 精度 | 中间位置的手指必须精确放在正中间 |
| Magic Mouse 距离要求 | 两指间距太远则不触发 |

### 1.8 检测算法推断

虽然 BTT 闭源，但从行为和 marcbourlon/TipTap 开源 JS 库可推断核心逻辑：

```
1. 手指接触触控面 → 记录初始位置和时间戳
2. 持续监测每根手指的位移量
3. 当新手指接触时：
   a. 检查已有手指位移是否低于"静止阈值" → 判定为"固定手指"
   b. 计算新手指与固定手指中心的水平相对位置 → 判定 Left/Right/Middle
   c. 记录固定手指数量 → 判定 Fix 级别
4. 当新手指抬起（touch-up）时：
   a. 检查 down→up 时间是否在"tap 时间窗口"内
   b. 检查新手指位移是否低于"tap 位移阈值"（排除滑动）
   c. 满足条件 → 触发 TipTap 事件
5. 边界处理：
   - 人类手指时间不精确性
   - 固定手指轻微自然抖动
   - 区分"即将开始滚动"和"固定手指+轻点"
```

marcbourlon/TipTap JS 库（MIT 开源）的核心设计：将 "tip"（持续触摸）和 "tap"（短触）作为两个原语，通过"等待窗口"处理人类手指时间不精确问题。

---

## 2. 实现约束与当前前提

### 2.1 当前输入层前提

- 当前文档基于 GestureSign 已修复后的输入链路进行讨论。
- 先前与多指触点相关的问题，主要来自 GestureSign 自身对 HID LinkCollection 与触控板报告的解析缺陷，而不是当前目标设备天然只能提供不完整数据。
- 在当前目标设备与当前版本 GestureSign 上，已实测验证 2/3/4/5 指触点可完整上送，能够获得独立的 `ContactID`、坐标与手指数信息。

### 2.2 当前部署前提

- GestureSign 以 `UIAccess` 模式部署运行；
- GestureSign 已阻断 Windows 默认多指手势；
- Windows 系统设置中的 2/3/4/5 指系统手势已禁用。

因此，本方案**无需再把“规避系统拦截”作为设计约束**，讨论重点应放在 GestureSign 内部如何正确识别和编排 TipTap。

### 2.3 当前真正的实现约束

- TipTap 需要区分 `Fix fingers` 与 `Tap finger` 的时序关系；
- 当前 `PointCapture` 以普通手势为中心设计，存在“任一手指抬起就结束本次捕获”的默认行为；
- 现有离散手势走 `PointPattern` 轨迹匹配，连续手势走 `ContinuousGestureTrigger`，而 TipTap 两者都不完全适配；
- 当前上层使用的是屏幕坐标，如需做更稳的 deadzone / 阈值归一化，后续仍可补充触控板原始或归一化坐标通路。

### 2.4 参考项目

- [RawInput.Touchpad](https://github.com/emoacht/RawInput.Touchpad)（C# 触控板原始数据解析示例）
- [ThreeFingerDragOnWindows](https://github.com/ClementGre/ThreeFingerDragOnWindows)
- [precision-touchpad-advanced-gestures](https://github.com/jrymk/precision-touchpad-advanced-gestures)

## 3. GestureSign 当前输入架构分析

### 3.1 输入管线

```
Windows WM_INPUT → MessageWindow.cs
  → TouchPadDevice.GetRawDatas() — 解析 HID，提取所有触点的 (ContactID, Point, TipSwitch)
    → InputProvider.PointsIntercepted 事件 — 传递 List<RawData>
      → PointEventTranslator.TranslateTouchEvent() — 判定 PointDown/Move/Up
        → PointCapture — 状态机管理手势捕获
          → GestureManager — 离散手势匹配
          → ContinuousGestureTrigger — 连续手势处理
```

### 3.2 当前数据可用性

GestureSign 从 HID 报告中提取的每触点数据（`RawData` 结构体）：
```csharp
struct RawData {
    DeviceStates State;        // Tip (触摸中) | None (已抬起)
    int ContactIdentifier;     // 手指唯一 ID
    Point RawPoints;           // 坐标
}
```

**可用数据**：
- ✅ 位置 (X, Y)
- ✅ Contact ID（手指标识）
- ✅ 触摸状态（Tip = 触摸中, None = 已抬起）
- ✅ 手指数量（总活跃手指数）
- ✅ 时间（ContinuousGestureTrigger 中的 Stopwatch 隐式提供）

**不可用数据**：
- ❌ 压力/力度
- ❌ 触点面积
- ❌ 手指身份（Contact ID 是瞬态的，跨手势会复用）
- ❌ 角度/朝向

### 3.3 关键事实：当前版本 GestureSign 已可获取完整多指触点数据

`TouchPadDevice.GetRawDatas()` 当前已能从触控板输入链路中提取完整多指触点数据；上层真正丢失多指信息的地方不在 HID 解析，而在 `PointCapture.TryBeginCapture()` 之后的特征手指轨迹建模。

数据丢失发生在 `PointCapture.TryBeginCapture()` 阶段——只有特征手指的轨迹被记录到 `_pointsCaptured`，其他手指的坐标数据被丢弃，仅保留总手指数。

### 3.4 特征手指机制

`PointCapture.TryBeginCapture()` 中：
```csharp
// ≤2 指：选最左边的手指作为特征手指
// >2 指：选从左数第二个手指（避免拇指边缘噪声）
int configuredIndex = _totalFingerCount <= 2 ? 0 : 1;
var sortedByX = firstPoint.OrderBy(p => p.Point.X).ToList();
featureFingers = new List<InputPoint> { sortedByX[actualIndex] };
```

### 3.5 PointEventTranslator 事件判定

```
rawData.Count > lastCount → PointDown（新手指触摸）
rawData.Count == lastCount → PointMove（手指移动）
rawData.Count < lastCount → PointUp（手指抬起）
```

基于**原始报告中的触点数量变化**，不是基于单个 Contact ID 的生命周期。

### 3.6 手势状态机

```csharp
enum CaptureState {
    Ready,              // 空闲
    Disabled,           // 系统锁定/挂起
    Capturing,          // 有效手势进行中
    CapturingInvalid,   // 潜在手势，等待验证
    TriggerFired        // 动作已触发，等待手指释放
}
```

### 3.7 现有手势类型

**离散手势**：用户自定义轨迹（PointPattern 匹配），如滑动、画圈等
**连续手势**：2/3/4 指滚动、缩放，方向命令（InertialScroll / Custom 模式）

---

## 4. TipTap 检测算法设计

### 4.1 核心原理

TipTap 的本质是识别以下时序模式：
```
固定手指(们): ─────────────────────────── (持续触摸，不移动)
轻点手指:          ┌──┐     ┌──┐          (短暂触摸-抬起，可重复)
                   tap1     tap2
```

需要区分的关键要素：
1. **时间先后**：固定手指先于轻点手指触摸
2. **静止判定**：固定手指在整个过程中位移接近零
3. **轻点判定**：新手指触摸后快速抬起
4. **方向判定**：轻点手指相对于固定手指群的水平位置

### 4.2 方案概述

TipTap 不是基于轨迹匹配的手势（不适合现有 PointPattern 引擎），而是**基于时序和空间关系的结构化手势**。应作为独立识别模块，与现有离散/连续手势识别并行工作。

**数据来源**：当前版本已可通过现有输入链路获取完整 `List<RawData>`。TipTap 的重点不再是修补底层 HID 解析，而是设计其与现有手势编排的关系。

### 4.3 检测状态机

```
                    ┌───────────────────────────────────────────┐
                    │                                           │
                    ▼                                           │
[Idle] ──finger down──▶ [Monitoring] ──timeout/move──▶ [Idle]  │
                            │                                   │
                       new finger down                          │
                       (tip fingers still)                      │
                            │                                   │
                            ▼                                   │
                     [TapDetecting] ──finger up (quick)──▶ [TipTapFired] ──▶ [Monitoring]
                            │                                   │
                       timeout/move                        all fingers up
                            │                                   │
                            ▼                                   ▼
                         [Idle]                              [Idle]
```

状态说明：
- **Idle**：无活跃触点
- **Monitoring**：有手指在触控面上，监测是否保持静止（候选 Tip 手指）
- **TapDetecting**：新手指触摸，等待判定是否为快速 Tap
- **TipTapFired**：TipTap 触发，回到 Monitoring 等待下一次 Tap（支持连续触发）

### 4.4 关键参数

| 参数 | 建议默认值 | 说明 |
|------|-----------|------|
| TipStillThreshold | 15px | Tip 手指位移阈值，超过则不认为静止 |
| TapMaxDuration | 200ms | Tap 手指从 down 到 up 的最大时间 |
| TipMinHoldTime | 80ms | Tip 手指在 Tap 之前必须至少持续的时间 |
| DirectionDeadzone | 20% | Left/Right/Middle 方向判定的中间死区比例 |

### 4.5 方向判定算法

```
tipCenterX = 所有 Tip 手指的 X 坐标中心
tapX = Tap 手指的 X 坐标
relativeX = tapX - tipCenterX

if relativeX < -deadzone: direction = Left
if relativeX > +deadzone: direction = Right
else: direction = Middle
```

### 4.6 与现有架构的集成点

如果只从“识别逻辑独立”角度出发，TipTap 似乎可以挂载在 `PointEventTranslator` 事件上并与 `PointCapture` 并行运行；但从当前 GestureSign 架构看，这种做法并不理想：

```
PointEventTranslator
  ├── PointCapture（现有：离散/连续手势）
  └── TipTapDetector（新增：TipTap 手势）
         └── 触发时调用 ApplicationManager 匹配 + PluginManager 执行
```

---

## 5. Windows 触控板特有挑战与应对

### 5.1 与两指滚动的区分（核心难题）

两指滚动和 1 Finger Fix TipTap 都涉及两根手指，时序相似。

**应对策略**：
- Tip 手指必须**先于** Tap 手指至少 `TipMinHoldTime` 毫秒到达
- Tip 手指在 Tap 手指出现前和出现期间的累计位移 < `TipStillThreshold`
- 两指几乎同时到达时不判定为 TipTap（这也是 BTT 的核心难题）

### 5.2 与当前 GestureSign 状态机的冲突

当前最大的工程问题不是系统拦截，而是 TipTap 与 `PointCapture` 现有结束逻辑之间的冲突：

- TipTap 的语义是：Fix fingers 仍然保持，Tap finger 先抬起；
- 但 `PointCapture` 当前默认行为是：在等待期或捕获期内，任一手指抬起都会走结束路径；
- 如果不先解决这个问题，Tap finger 抬起时 TipTap session 会被现有普通手势流程提前终止。

**应对策略**：TipTap 识别逻辑可以独立，但必须纳入 `PointCapture` 的统一编排之下，不能简单完全并行。

### 5.3 数据精度不足

Windows 不提供速度和触点形状。

**应对策略**：
- 用 Tip Switch 的 down/up 时间差判定 Tap（而非速度）
- 用累计位移判定 Tip 静止（而非瞬时速度）
- HID 报告的 Scan Time（100μs 精度）提供足够时间分辨率

### 5.4 触点数量限制

Windows Precision Touchpad 最多 5 个触点，但 3 Finger Fix（4 指）仍在范围内。

### 5.5 特征手指机制冲突

当前 PointCapture 只跟踪特征手指轨迹，丢弃其他手指数据。

**应对策略**：TipTap 检测器独立于 PointCapture，直接订阅事件获取完整 `List<RawData>`，不依赖特征手指选择。

---

## 6. 可行性结论

### 6.1 实测验证：触控板数据特征（2026-03-10）

#### 第一轮测试：坐标可用性

在实际设备上通过 HID 诊断日志验证，结果如下：

**3 指手势**（3 指静置在触控板上）：

```
HID Frame: slots=3, details=[ID=0 State=Tip X=1770 Y=518, ID=1 State=Tip X=1500 Y=964, ID=0 State=None X=0 Y=0]
```

- 3 个槽位（表示检测到 3 根手指），但只有 ID=0 和 ID=1 有有效坐标
- 第 3 个槽位 `State=None X=0 Y=0`，无坐标数据

**4 指手势**（4 指静置在触控板上）：

```
HID Frame: slots=4, details=[ID=0 State=Tip X=2047 Y=365, ID=1 State=Tip X=1627 Y=525, ID=0 State=None X=0 Y=0, ID=0 State=None X=0 Y=0]
```

- 4 个槽位（表示检测到 4 根手指），但只有 ID=0 和 ID=1 有有效坐标
- 第 3、4 个槽位 `State=None X=0 Y=0`，无坐标数据

**结论**：触控板硬件/驱动**最多只提供 2 个手指的实际坐标**。其余手指只有"存在"信息（通过槽位数体现），没有位置数据。

#### 第二轮测试：输入层问题已修复

此前文档中的若干推论，建立在“第 3+ 手指没有独立坐标”“只能依赖 count 变化”的旧观察之上；这些结论已随着输入层修复而过时。

当前实测结果表明：

- 2/3/4/5 指触点均可获得独立坐标；
- `FingerCount` 与实际输入点数可对齐；
- 现阶段不应再把“第 3+ 手指没有坐标”作为 TipTap 设计前提。

### 6.2 基于完整多指坐标的方向判定方案

在当前输入层已能提供完整多指坐标的前提下，TipTap 的方向判定应建立在完整 Fix/Tap 手指坐标之上，而不是依赖旧的 count 变化补偿模型。

核心思路：

- 先识别 Tip/Fix fingers 与 Tap finger 的时序关系；
- 使用 Tap finger 相对于 Fix fingers 锚点的位置判定 Left / Right / Middle；
- 锚点可取 Fix fingers 的质心、中位数或平均 X 值；
- Middle 判定通过 deadzone 控制，而不是依赖“无方向 TipTap”降级模型。

### 6.3 最终可行性判定

| TipTap 变体 | 方向 | 可行性 | 说明 |
|---|---|---|---|
| **1 Finger Fix Left/Right** | ✅ 精确 | **✅ 完全可行** | 2 个手指都有坐标，直接比较 X 判定方向 |
| **2 Finger Fix Left/Right/Middle** | 需判定 | **✅ 可设计实现** | 当前输入层已能提供完整第 3 指坐标，可按 Tip + Tap 模型直接判定 |

| **3 Finger Fix Left/Right/Middle** | 需判定 | **✅ 可设计实现** | 当前输入层已能提供完整第 4 指坐标，关键问题转为状态机编排 |


**建议实现优先级**：

1. **1 Finger Fix Left/Right**（2 指）— 最适合作为第一阶段实现，用于先验证 TipTap 的时序模型与识别链路
2. **2 Finger Fix Left/Right/Middle**（3 指）— 在当前输入层前提下可直接纳入设计讨论
3. **3 Finger Fix Left/Right/Middle**（4 指）— 理论上也可成立，重点在状态机与阈值调优

---

## 7. 最终设计原则（综合 Claude Code 调研 + Codex 工程建议）

经过两份方案的交叉复核与合并，最终设计原则如下：

### 语义层

- 采用 **Tip + Tap 时序模型**：Tip 手指先落下并保持相对静止，Tap 手指后落下并快速抬起
- 方向按 **Tap 相对 Fix fingers 的位置**判定（Left/Right/Middle），不是触控板绝对区域
- 按 BTT 语义建模为 **N Finger Fix + Left/Right/Middle 族**，具体支持哪些组合以最终核实的触发器集合为准（注：1 Finger Fix 可能只有 Left/Right，不一定有 Middle）
- 建议优先实现 1 Finger Fix（2 指），原因是它最适合作为第一阶段验证 TipTap 状态机，而不是为了规避系统手势

### 架构层

- TipTap 是**全新的识别语义**，不适合塞进现有 `PointPattern` 轨迹匹配链路，也不应复用现有连续手势方向识别逻辑
- 但 TipTap **不应该完全脱离 PointCapture 另起一套并行总控状态机**。更合理的方式是：
  - 新增独立 `TipTapRecognizer` 作为识别模块；
  - 由 `PointCapture` 负责总编排、输入会话生命周期和与现有手势链路的协调；
  - `TipTapRecognizer` 在 `PointCapture` 控制下消费早期输入窗口并给出 TipTap 候选/命中/失败结果。
- 原因是当前 `PointCapture` 已经掌控：
  - `MultiFingerDelay`
  - 捕获状态机（`Ready / CapturingInvalid / Capturing / TriggerFired`）
  - `PointDown / PointMove / PointUp` 生命周期
  - `CaptureStarted / EndCapture / GestureRecognized` 编排
- 如果完全平行挂接在 `InputProvider` 或 `PointEventTranslator` 上，容易出现两套状态机竞争同一波输入、重复消费、或在 `PointUp -> EndCapture` 时被当前逻辑提前打断。
- 不推翻现有特征手指机制——特征手指继续服务轨迹手势；TipTap 使用独立识别模块和独立 session 数据，但由 `PointCapture` 统一编排。

### 鲁棒性层

- 当前目标设备与当前 GestureSign 输入链路下，2/3/4/5 指触点数据已可视为完整且可用；TipTap 方案不再以“不完整触点帧”作为核心设计前提。
- **引入触控板归一化坐标**：用于距离阈值、deadzone、静止阈值的一致化（不同设备/分辨率下行为稳定），注意归一化坐标是辅助基础设施，不是用于绝对区域分区
- TipTap 方向与位置判定可直接建立在完整的 Fix fingers / Tap finger 坐标之上，不再需要围绕“第 3+ 手指无坐标”的旧降级模型设计。

### 与现有手势实现的关系

- TipTap 从理论上应视为一种**新的离散时序手势类型**，而不是：
  - 现有离散轨迹手势（`PointPattern`）的一个变种；
  - 现有连续手势（滚动/缩放/方向连续命令）的一个特例。
- 因此它更适合采用“**识别逻辑独立、编排逻辑整合**”的模式：
  - **识别逻辑独立**：单独维护 Tip/Fix fingers、Tap finger、候选状态、时序阈值、方向判定；
  - **编排逻辑整合**：仍由 `PointCapture` 决定这波输入最终走 TipTap、普通离散手势，还是连续手势。
- 推荐的理论分层如下：
  - `InputProvider / PointEventTranslator`：原始输入与基础事件翻译
  - `PointCapture`：统一输入会话编排中心
  - `TipTapRecognizer`：TipTap 专用识别器
  - `PointPattern / ContinuousGestureTrigger`：现有离散轨迹与连续手势处理器
- 具体决策建议：
  - 在 `PointCapture` 的早期窗口（尤其是 `MultiFingerDelay` 期间）启动 TipTap session；
  - 若识别器判断为 TipTap 候选，则暂缓现有“任一手指抬起即 EndCapture”的普通结束逻辑；
  - 若候选失败，再把输入交还给现有普通手势或连续手势流程。
- 这意味着：
  - TipTap **不是简单并行**；
  - 也**不是硬整合进旧识别算法**；
  - 而是“在现有编排框架中新增一条专门处理 TipTap 的识别分支”。

### 当前部署前提

- 当前讨论基于如下前提：
  - GestureSign 以 `UIAccess` 方式部署运行；
  - GestureSign 已阻断 Windows 默认多指手势；
  - Windows 系统设置中 2/3/4/5 指手势已禁用。
- 因此在当前方案中，**无需再把“规避系统拦截”作为设计约束或第一阶段范围控制理由**。
- 当前真正需要解决的是 GestureSign 内部的手势编排问题，尤其是 TipTap 与现有 `PointCapture` / 连续手势链路的协作关系。

### 与两指滚动的区分（核心难题）

这是 BTT 近十年未完全解决的问题，需要特别关注：

- Tip 手指必须先于 Tap 手指至少 `TipMinHoldTime` 到达
- Tip 手指在 Tap 手指出现前和出现期间的累计位移 < `TipStillThreshold`
- 两指几乎同时到达时不判定为 TipTap
- 建议提供灵敏度调节选项

---

## 8. 参考资源

- [BetterTouchTool 官方文档 - Magic Mouse & Trackpad Triggers](https://docs.folivora.ai/docs/601_magic_mouse_trackpad.html)
- [BTT Community: What is TipTap](https://community.folivora.ai/t/what-is-tiptap-left-centre-middle-2-finger-fix/18237)
- [BTT Community: False detecting TipTap until scroll](https://community.folivora.ai/t/false-detecting-tiptap-until-scroll-with-two-fingers/6355)
- [BTT Community: TipTap seems much harder to tune lately](https://community.folivora.ai/t/tip-tap-seems-much-harder-to-tune-lately/31858)
- [GestureSign Issue #47: Tip-tap gesture support](https://github.com/TransposonY/GestureSign/issues/47)
- [marcbourlon/TipTap JS 库](https://github.com/marcbourlon/TipTap)（MIT 开源参考）
- [Windows Precision Touchpad HID 规范](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-windows-precision-touchpad-collection)
- [Windows Precision Touchpad - Maximum Supported Contacts](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-maximum-supported-contacts)
- [Windows Precision Touchpad - Confidence Reporting](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-confidence-reporting)
- [Windows Precision Touchpad - Selective Reporting](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-selective-reporting)
- [macOS MultitouchSupport.h](https://github.com/asmagill/hs._asm.undocumented.touchdevice/blob/master/MultitouchSupport.h)
- [Touching Apple's Private Multitouch Framework](https://medium.com/ryan-hanson/touching-apples-private-multitouch-framework-64f87611cfc9)
- [RawInput.Touchpad](https://github.com/emoacht/RawInput.Touchpad)（C# 触控板数据解析示例）
- [ThreeFingerDragOnWindows](https://github.com/ClementGre/ThreeFingerDragOnWindows)
- [precision-touchpad-advanced-gestures](https://github.com/jrymk/precision-touchpad-advanced-gestures)
- [Firefox Source Docs - Windows Pointing Device Support](https://firefox-source-docs.mozilla.org/widget/windows/windows-pointing-device/index.html)



