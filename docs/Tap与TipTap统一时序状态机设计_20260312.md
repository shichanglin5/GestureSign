# GestureSign Tap / TipTap 统一时序状态机设计

> 日期: 2026-03-12
> 状态: 待评审
> 目标: 先统一设计，再进行 Tap / TipTap 识别重构编码

---

## 1. 文档目标

本文档用于明确 GestureSign 中 Tap / TipTap 的统一识别语义、状态机设计、录制与运行时的共用实现方向，并结合 BetterTouchTool（BTT）公开行为和社区反馈，整理可借鉴点与应避免的问题。

当前结论：

- Tap 与 TipTap 本质上是同一个 contact session 中的两种互斥解释；
- 识别的核心不应是“轨迹长什么样”，而应是“手指以什么顺序落下、保持、抬起”；
- 录制与运行时不能再分别维护两套语义模型，而应共用同一套时序状态机；
- 当前 GestureSign 中 TipTap 的“固定手指先确认，再允许 tap 手指落下”的模型不自然，需废弃。

---

## 2. 背景与当前问题

### 2.1 当前实现存在的结构问题

当前 Tap / TipTap 相关逻辑分散在以下文件：

- `GestureSign.Daemon/Input/PointCapture.cs`
- `GestureSign.Daemon/Input/TipTapRecognizer.cs`
- `GestureSign.Daemon/Input/TipTapRuntimeState.cs`
- `GestureSign.Daemon/Input/GestureClassifier.cs`

现状问题不是单一阈值错误，而是识别模型本身不统一：

1. 运行时识别：依赖 `PointCapture + TipTapRecognizer` 的实时状态切换。
2. 录制分类：依赖 `GestureClassifier` 在手势结束后按轨迹结果反推 Tap / TipTap / Trajectory。
3. TipTap 现有模型隐含要求“固定手指先稳定保持，再由 tap 手指后到并快速抬起”，这与用户真实动作并不一致。
4. 当前录制与运行时可能对同一组输入给出不同结论。

### 2.2 当前用户体验上的主要异常

从最近实际测试反馈看，当前实现会出现以下问题：

- 多指 TipTap 经常被误识别为普通 Tap；
- 三指 TipTap Left / Middle / Right 录制不稳定，常被误识别为轨迹手势；
- 录制时的 TipTap 成功与否，取决于非常苛刻的手指时序细节；
- 录制链路与执行链路的语义不一致，导致“录得出来但执行不稳”或“执行能识别但录制分错类”。

这些问题说明：当前系统没有在识别“手指时序语义”，而是在同时猜测 Tap、TipTap 与 Trajectory。

---

## 3. 用户真实动作模型

### 3.1 普通多指 Tap

普通多指 Tap 的用户动作语义是：

1. 第一根手指落下；
2. 其余手指可在同一短时窗口内陆续落下，允许人手自然滚落式近同步；
3. 所有参与手指都在 Tap 窗口内抬起；
4. 整体触点位移较小。

关键特征：

- 所有参与手指都属于同一波短时接触；
- 不存在“固定手指持续保留，tap 手指先离开”的结构；
- 是否属于 Tap，不再额外要求“最后一根 down 也必须落在单独的 simultaneity window 内”；
- Tap 的核心时间判据就是“第一根手指 down 到所有参与手指全部 up 的总时间”；
- 若第一根手指落下到最后一根手指抬起已经明显超过 Tap 阈值，则不应再被识别为普通 Tap。

### 3.2 TipTap

TipTap 的用户动作语义是：

1. 一根或多根固定手指先落下；
2. 固定手指保持按住且基本不动；
3. 另一根手指在其左/中/右位置快速点一下；
4. tap 手指先抬起；
5. 固定手指稍后仍可继续保留或再抬起。

关键特征：

- tap 手指必须是“最后落下的那根手指”；
- tap 手指本身必须满足普通 tap 的 down-up 窗口与位移阈值；
- 当 tap 判断窗口到达时，如果所有手指都已抬起，则应优先识别为普通 Tap；
- 当 tap 判断窗口到达时，如果仍有手指保持按下，则普通 Tap 路径关闭，转为 TipTap 路径；
- TipTap 与 Tap 不是并行手势，而是同一 contact session 的互斥分支。

---

## 4. 设计原则

### 4.1 Tap / TipTap 必须统一编排

Tap 与 TipTap 的关系应为：

- 共享同一个 contact session；
- 共享同一个“等待窗口”；
- 到达判定时间点后按剩余接触状态进行分流；
- 不应由两个互不相干的识别器并行抢结果。

### 4.2 以时序为主，以轨迹为辅

Tap / TipTap 的主判据应为：

- 手指 down 顺序；
- 手指 up 顺序；
- 第一根手指 down 到全部手指 up 的总时长；
- TipTap 中最后落下手指自身的 down-up 时长；
- Tap 窗口结束时仍保留在板上的手指集合；
- 相对空间位置（用于 TipTap Left / Middle / Right）；
- 位移阈值仅用于去抖和排除滑动，不应成为主语义来源。

### 4.3 录制与运行时必须共用同一套语义

录制时不应在结束后通过轨迹推断“像不像 TipTap”，而应直接复用运行时状态机输出的结构化结果：

- Gesture kind: Tap / TipTap / Trajectory
- Finger count
- Fix finger ids / Tap finger id
- Tap finger down / up 时间
- Fix anchor
- Direction

`GestureClassifier` 最终只应负责把“已确认的语义结果”转换为定义对象，而不再承担主识别职责。

### 4.4 不再使用“固定手指必须先确认一段时间再允许 tap 手指落下”的模型

这是当前实现最不自然的地方之一。更合理的方式是：

- 从第一根手指落下开始进入统一等待窗口；
- 在等待窗口内，不立即触发 Tap，也不立即触发 TipTap；
- 到窗口结束再统一判定：若全抬起则判 Tap，否则看是否满足 TipTap 条件。

这样符合真实用户操作，也能减少奇怪的时序要求。

---

## 5. 统一状态机设计

### 5.1 状态机概览

建议新增一个统一的 `ContactSessionRecognizer`，由 `PointCapture` 控制其生命周期。

状态如下：

```text
Idle
  └─ 第一根手指落下 -> ContactWindow

ContactWindow
  ├─ 在 TapWindow 内持续收集 down / move / up
  ├─ 若明显超出 Tap/TipTap 位移边界 -> TrajectoryCandidate
  └─ TapWindow 到期 -> EvaluateContactWindow

EvaluateContactWindow
  ├─ 若所有参与手指都已抬起 -> TapMatched / NotTap
  ├─ 若仍有固定手指保持按下 -> TipTapMonitoring
  └─ 若结构不满足 -> TrajectoryCandidate / Unknown

TipTapMonitoring
  ├─ 若最后落下手指在短时间内先抬起 -> TipTapMatched
  ├─ 若固定手指移动过大 -> TrajectoryCandidate / Cancelled
  ├─ 若 tap 手指停留过久 -> NotTipTap
  └─ 若整体结束仍未满足 -> Unknown / Trajectory

TapMatched / TipTapMatched / TrajectoryCandidate / Cancelled
  └─ Session reset -> Idle
```

### 5.2 关键判定窗口

#### TapWindow

Tap 与 TipTap 共用的第一个关键窗口。

定义：

- 起点：第一根手指 down；
- 长度：`TapMaxDurationMs`；
- 用途：
  - 在窗口到达前，不触发 Tap 与 TipTap；
  - 到达窗口时再统一判断所有手指当前状态。

#### TipTapTapFingerWindow

用于约束 tap 手指本身是否是一次真正的 tap。

定义：

- 起点：最后落下手指的 down；
- 长度：`TapMaxDurationMs` 或 `TipTapTapMaxDurationMs`；
- 用途：
  - 限定最后落下手指必须是一次短触；
  - 超时则不能视为 TipTap。

### 5.3 Tap 的判定规则

在 `EvaluateContactWindow` 时：

满足以下条件则判定为 Tap：

1. 所有参与手指都已抬起；
2. 第一根手指 down 到最后一根手指 up 不超过 `TapMaxDurationMs`；
3. 每根手指移动距离不超过 `TapMaxMovementPx`；
4. 若 TapWindow 到期时仍有任何手指保持按下，则必须直接排除 Tap。

若超过 Tap 阈值，则不应再被识别为 Tap。

补充说明：

- Tap 不再单独引入 `down simultaneity window`；
- 只要整体仍满足“短时、低位移、最终全部抬起”，就允许人手自然滚落式近同步落指；
- 与 TipTap 的分界不靠额外的 Tap down 同步阈值，而靠“是否全部抬起”以及 TipTap 的 hold delay 结构约束。

### 5.4 TipTap 的判定规则

在 `TapWindow` 到期时，如果仍有手指按在板上，则不再允许普通 Tap 触发，转入 TipTap 路径。

满足以下条件则判定为 TipTap：

1. 存在一根“最后落下的手指”；
2. 这根手指不是与 fix fingers 几乎同时落下，而是晚于最后一根固定手指至少一个最小保持时长；
3. 这根手指的 down-up 时长不超过 tap 阈值；
4. 这根手指是最先抬起的那根手指，且抬起时其他固定手指仍保持按下；
5. 固定手指在整个窗口中位移低于固定指静止阈值；
6. tap 手指本身位移低于 tap 位移阈值；
7. tap 手指相对固定指 anchor 的水平位置决定 `Left / Middle / Right`。

这里新增强调一个之前文档遗漏、但代码里已经实际使用的结构约束：

- `tapDownMs - latestFixDownMs >= TipTapFixMinHoldMs`

它的语义不是“固定手指必须先稳定很久才允许识别 TipTap”，而是一个最小结构门槛：

- 用来要求 tap finger 明确晚于 fix fingers 出现；
- 用来排除“几根手指滚落式近同步 down，但其中一根先抬起”的普通多指 Tap / rolled tap；
- 用来避免把几乎同时落下的多指接触误解释成 TipTap。

这个约束属于 TipTap 的结构判据，不属于旧 `MultiFingerDelay` 的早期观察窗口。两者的职责不同：

- `MultiFingerDelay` 负责在 session 早期收集同一波 contact；
- `TipTapFixMinHoldMs` 负责在进入 TipTap 分支后，确认 tap finger 与 fix fingers 之间存在最小先后差。

### 5.5 方向定义

以固定指群中心点 `fixAnchorX` 为基准：

- `tapX < fixAnchorX - deadzone` -> `Left`
- `tapX > fixAnchorX + deadzone` -> `Right`
- 否则 -> `Middle`

建议使用：

- 优先用 tap 手指 down 时坐标或 down-up 期间平均坐标；
- 固定指 anchor 用 TapWindow 到期时仍按下的固定指集合的平均位置；
- 不使用轨迹终点单点作为唯一方向依据，避免抖动带来卡方向。

---

## 6. 录制与运行时的统一实现方案

### 6.1 统一输出结构

建议引入统一的 contact session 结果结构，例如：

```csharp
ContactSessionResult {
    ContactGestureKind Kind;           // None / Tap / TipTap / Trajectory
    int FingerCount;
    int TotalFingerCountSeen;
    IReadOnlyList<int> FingerDownOrder;
    IReadOnlyList<int> FingerUpOrder;
    IReadOnlyList<int> FixFingerIds;
    int? TapFingerId;
    DateTime SessionStartedAtUtc;
    DateTime? TapFingerDownAtUtc;
    DateTime? TapFingerUpAtUtc;
    Point? FixAnchor;
    ContactGestureDirection Direction;
    GestureSessionSnapshot Snapshot;
}
```

### 6.2 运行时链路

运行时执行建议流程：

1. `PointCapture` 维护统一 `ContactSessionRecognizer`；
2. contact session 在早期窗口内持续收集触点；
3. 状态机在合适时机给出 `TapMatched` 或 `TipTapMatched`；
4. 命中后由 `PointCapture` 决定是否触发对应配置；
5. 若未命中再交由普通轨迹 / 连续手势链路。

### 6.3 录制链路

录制建议流程：

1. Training 模式也走同一 `ContactSessionRecognizer`；
2. recognizer 输出 `Tap / TipTap / Trajectory` 结果；
3. 若为 Tap / TipTap，直接生成对应 `TapGestureConfig / TipTapGestureConfig`；
4. `GestureClassifier` 只作为薄转换层，或逐步废弃。

### 6.4 对当前代码的影响

需要重构的重点：

- `PointCapture.cs`
  - 合并 Tap / TipTap 的早期编排；
  - 移除 `_pendingFirstPoints` 与 `_tipTapSessionActive` 的当前分裂模型；
  - 改为统一 contact session。

- `TipTapRecognizer.cs`
  - 不再作为独立分支会话模型；
  - 变为统一 session 状态机中的 TipTap 判定模块，或直接合并进 `ContactSessionRecognizer`。

- `GestureClassifier.cs`
  - 去除对 TipTap 的后验轨迹推断职责；
  - 保留定义构造或兼容过渡逻辑。

- `TapGestureRecognition / TipTapRecognition`
  - 参数语义要重新梳理，避免重复和互相冲突。

---

## 7. BetterTouchTool 调研结论与借鉴点

本节基于以下在线资料：

- BTT Community: `what is tiptap left, centre, middle, 2 finger fix?`
- BTT Community: `False detecting TipTap until scroll with two fingers`
- BTT Community: `Tip tap seems much harder to tune lately`
- 开源项目 `marcbourlon/TipTap`

### 7.1 BTT 可确认的行为特征

从社区描述可确认：

1. BTT 的 TipTap 语义与我们当前理解一致：固定指保留，另一根手指在左/中/右快速轻点。
2. BTT 存在 `TipTap sensitivity` 之类的灵敏度调节，说明其识别高度依赖时序窗口与位移容忍度。
3. 1 Finger Fix TipTap 与两指滚动长期存在误触冲突，说明 TipTap 与滚动之间必须有严格互斥与排除机制。
4. 2/3 Finger Fix 并不是完全稳定，尤其在不同硬件上识别体验差异明显。

### 7.2 BTT 可以借鉴的点

#### 借鉴点 1：等待窗口 / 人类误差补偿

`marcbourlon/TipTap` 明确把“人类手指不可能完美同步”作为核心问题，并引入短暂等待窗口来补偿几乎同时但非完全同步的事件。

对 GestureSign 的启发：

- 第一根手指 down 后，不应立刻判 Tap 或 TipTap；
- 应有统一的等待窗口来吸收近似同步的人类动作误差；
- Tap 与 TipTap 应共享这个窗口，而不是各自独立等待。

#### 借鉴点 2：Tap 与 Tip 的层次化关系

开源 TipTap 库把 `tap`、`tip` 看作更基础的动作原语，再组合出 `tip-tap` 这类复杂动作。

对 GestureSign 的启发：

- TipTap 应视为“固定指持续 tip + 最后手指短时 tap”的结构化组合；
- 状态机应直接表达这种层次关系，而不是用轨迹分类去猜。

#### 借鉴点 3：不要过早发送可组合手势结果

开源 TipTap 库的 README 提到：为支持 combo/复杂手势，可组合类手势不会立即派发，而会等一个短暂窗口确认是否还有后续事件。

对 GestureSign 的启发：

- Tap / TipTap 不应在第一时间立即派发；
- 必须等统一等待窗口结束后再做最终分流，否则容易 Tap/TipTap 抢结果。

### 7.3 BTT 提醒我们要避免的坑

#### 坑 1：TipTap 与两指滚动混淆

BTT 社区长期有人反馈两指滚动被误识别为 TipTap Left / Right。

对 GestureSign 的设计约束：

- TipTap 与滚动不能只是“都试一遍”；
- 必须有明确的早期排除逻辑：
  - 若两指几乎同时落下并持续移动，应优先进入滚动候选；
  - 若最后落下手指不满足 tap 时长与位移限制，则不能判 TipTap。

#### 坑 2：灵敏度过多但语义不清

从 BTT 用户反馈看，单靠“灵敏度”滑杆并不能从根上解决模型不一致问题。

对 GestureSign 的设计约束：

- 先把 Tap / TipTap / Scroll 的状态机边界定义清楚；
- 再暴露少量真正有语义的参数，如：
  - `TapMaxDurationMs`
  - `TapMaxMovementPx`
  - `TipTapDirectionDeadzonePx`
  - `FixStillThresholdPx`
- 避免先上大量含糊的“灵敏度”参数。

#### 坑 3：把 TipTap 完全当作独立并行识别器

旧文档和当前实现都容易走向“新增一条 TipTap 分支并行识别”的方向，但从当前讨论看，这不是最优设计。

对 GestureSign 的结论：

- TipTap 不应与 Tap 并行竞争结果；
- TipTap 也不应完全脱离 `PointCapture` 编排；
- 更好的模型是：在统一 contact session 下，Tap / TipTap / Trajectory / Scroll 分阶段分流。

---

## 8. 建议的参数模型

建议最终参数收敛为以下几项：

### Tap 共享参数

- `TapMaxDurationMs`
  - 第一根手指 down 到所有参与手指全部 up 的最大总时间窗口
- `TapMaxMovementPx`
  - 任一参与手指被视为 tap 时允许的最大位移

当前结论：

- Tap 不再额外引入 `TapSimultaneityWindowMs`；
- 若后续需要支持 UI 调整，优先暴露 `TapMaxDurationMs` 与 `TapMaxMovementPx`，而不是再增加独立 down 同步窗口。

### TipTap 参数

- `TipTapFixMinHoldMs`
  - 最后一根固定手指 down 到 tap 手指 down 的最小间隔
  - 用于排除近同步 rolled tap，被视为 TipTap 的最小结构门槛
- `TipTapTapMaxDurationMs`
  - 最后落下手指自身 down-up 允许的最大时间
- `FixStillThresholdPx`
  - 固定指整体静止容忍阈值
- `DirectionDeadzonePx`
  - Left / Middle / Right 的水平死区

### 与滚动分流相关的参数

- `EarlyMoveRejectPx`
  - 早期若出现明显持续移动则退出 Tap/TipTap 候选
- `ScrollSimultaneousDownWindowMs`
  - 多指几乎同时落下并进入持续移动时，优先判为滚动候选

### 8.1 当前代码中的默认参数（2026-03-13）

当前实现已经部分落地，且训练与运行时主语义保持一致。默认参数分为两类：

1. 配置对象中的运行时参数

Tap:

- `TapGestureRecognition.MaxDurationMs = 220`
- `TapGestureRecognition.MaxMovementPx = 18`
- `TapGestureRecognition.MinFingerCount = 2`

TipTap:

- `TipTapRecognition.FixMinHoldMs = 50`
- `TipTapRecognition.MaxTapDurationMs = 140`
- `TipTapRecognition.FixStillThresholdPx = 12`
- `TipTapRecognition.TapMaxMovementPx = 18`
- `TipTapRecognition.DirectionDeadzonePx = 24`
- `TipTapRecognition.RepeatCooldownMs = 60`

1. 分析阶段辅助阈值

`GestureAnalyzer` 当前还承担一层共享的 Tap-like 辅助判断，默认值为：

- Tap-like 总时长窗口：`300ms`
- 严格单指位移阈值：`18px`
- 对 `4` 指及以上 Tap，允许一个离群手指放宽：
  - 多数手指位移阈值：`20px`
  - 单离群手指最大位移：`30px`
  - 同时要求整体平均位移 `< 18px`

这组 `4` 指及以上放宽规则是为了吸收高指数组合下更常见的单指抖动；它不是训练特判，录制和运行时都应共享。

### 8.2 当前实现约束

- 会话切段属于 `PointCapture` 的共享生命周期语义，训练模式不能单独维护另一套 session 边界。
- 训练模式允许增加诊断输出、原始帧、分类说明等排障信息，但不允许改变 Tap / TipTap / Trajectory 的主判定语义。
- Tap 与 TipTap 继续保持互斥：只要 Tap 满足，就优先归为 Tap。

### 8.3 后续需要支持 UI 配置的参数

后续 ControlPanel 需要把 Tap / TipTap 的参数从“代码默认值”提升为“可见、可恢复默认值”的配置项。

建议分两层开放：

第一层：默认展示的运行时参数

- Tap:
  - `MaxDurationMs`
  - `MaxMovementPx`
- TipTap:
  - `FixMinHoldMs`
  - `MaxTapDurationMs`
  - `FixStillThresholdPx`
  - `TapMaxMovementPx`
  - `DirectionDeadzonePx`

第二层：高级设置 / 实验性参数

- `RepeatCooldownMs`
- Tap-like 总时长窗口 `300ms`
- `4` 指及以上 Tap 的多数手指位移阈值 `20px`
- `4` 指及以上 Tap 的单离群手指阈值 `30px`
- 缺失时序元数据时是否允许录制分类走轨迹兜底

原则：UI 只允许配置共享识别参数，不允许引入“仅训练生效”的判定阈值。

---

## 9. 重构实施建议

### 9.1 重构边界

第一阶段只重构后端识别与录制逻辑，不碰 UI 表现层：

1. 新增统一 `ContactSessionRecognizer`
2. 运行时 Tap / TipTap 共用状态机
3. 录制链路复用同一状态机
4. `GestureClassifier` 退化为转换层
5. 保持现有 `TapGestureConfig / TipTapGestureConfig` 对外模型暂时兼容

### 9.2 不在第一阶段做的事情

- 不先暴露大量新参数到 UI
- 不先优化连续重复 TipTap 的高级体验
- 不先做复杂多轮 combo
- 不先追求与 BTT 完全一致的手感

目标是先让语义正确、录制与执行一致、边界清晰。

### 9.3 推荐实施顺序

1. 定义统一 contact session 数据结构
2. 在 `PointCapture` 中实现统一状态机骨架
3. 接入 Tap 识别
4. 接入 TipTap 识别
5. 用统一结果重接训练模式录制
6. 移除旧的后验 `GestureClassifier` TipTap 推断逻辑
7. 增加测试：
   - 两指普通 tap
   - 右固定 + 左 tap
   - 左固定 + 右 tap
   - 两固定 + 左 tap
   - Tap 超时不应命中
   - 最后落下手指不是先抬起时不应命中 TipTap
   - 两指滚动不应误命中 1-fix TipTap

---

## 10. 评审重点

本次评审建议重点确认以下问题：

1. 是否同意 Tap / TipTap 视为统一 contact session 下的互斥分支？
2. 是否同意废弃“固定手指先确认一段时间再允许 tap 指落下”的旧设计？
3. 是否同意录制与运行时共用同一状态机，而不是继续维持后验轨迹分类？
4. 是否同意第一阶段先不暴露过多 UI 参数？
5. 是否同意 TipTap 方向以最后落下手指相对固定指 anchor 的位置计算？

---

## 11. 当前结论

根据本轮讨论与 BTT 调研，GestureSign 下一步在 Tap / TipTap 上的正确方向不是继续补丁式修阈值，而是：

- 把 Tap 与 TipTap 合并为统一时序状态机；
- 以第一根手指 down 作为统一等待窗口起点；
- TapWindow 到达后优先判断“是否所有手指都已抬起”；
- 若未全部抬起，则关闭 Tap 路径，转入 TipTap 路径；
- 以“最后落下且先抬起的那根手指”为 tap finger；
- 录制和运行时共用这套语义。

在你 review 确认这份文档后，再进入代码重构阶段。
