# GestureSign 整体手势架构设计

## 1. 背景与目标

GestureSign 过去的多指手势实现，建立在两个重要前提上：

- 多指触点数据不完整，无法稳定依赖全量触点；
- 为了降低复杂度和规避解析缺陷，只保留“特征手指”轨迹进行后续匹配。

随着最近对 HID 触点解析链路的修复，这两个前提已经发生了变化：

- 当前目标设备上，2/3/4/5 指触点数据已经可以完整获取；
- 手势结束阶段的输入收敛也已明显改善；
- “任意一根手指抬起即结束手势”的旧逻辑，不再适合作为统一结束模型。

因此，GestureSign 需要重新梳理整体手势架构。

本设计文档的目标不是直接实现 TipTap，而是先回答：

- GestureSign 的手势体系应该如何重新分层；
- 现有实现中哪些逻辑是历史遗留，需要先修正；
- TipTap 未来应该放在什么位置，如何与现有体系协作。

---

## 2. 当前实现现状

## 2.1 当前主链路

当前输入到识别的主链路可以概括为：

```text
WM_INPUT / HID
  -> MessageWindow
  -> InputProvider
  -> PointEventTranslator
  -> PointCapture
     -> GestureManager / PointPattern
     -> ContinuousGestureTrigger
     -> PluginManager
```

其中：

- `MessageWindow` 负责原始 HID 输入解析与设备注册；
- `InputProvider` 负责输入事件桥接；
- `PointEventTranslator` 把原始触点转成 `PointDown / PointMove / PointUp`；
- `PointCapture` 负责全局输入捕获状态机；
- `GestureManager / PointPattern` 负责离散轨迹手势匹配；
- `ContinuousGestureTrigger` 负责连续手势（滚动、缩放、连续方向）处理。

## 2.2 当前 `PointCapture` 的核心特点

当前 `PointCapture` 既承担了：

- 输入会话生命周期管理；
- 多指延迟收集（`MultiFingerDelay`）；
- 手势开始/结束判定；
- 轨迹采样；
- UI 轨迹绘制；
- 与应用匹配、命令执行链路衔接。

这意味着 `PointCapture` 实际上已经是 GestureSign 的**手势编排中心**，而不是单纯的轨迹采样器。

## 2.3 当前设计的问题

当前设计有几个明显历史包袱：

### 1. 把“特征手指”当作全局事实，而不是局部策略

当前 `TryBeginCapture()` 中会根据手指数选一根特征手指，并只为它记录轨迹。

这在数据缺失时期是合理折中，但在当前输入链路已修复的前提下，问题在于：

- 系统层面已经拿到了全量触点；
- 但识别层面仍然把其他手指信息丢掉；
- 导致很多本来可表达的手势语义无法被建模。

### 2. 用统一的“任一手指抬起即结束”逻辑处理所有手势

当前在等待阶段和捕获阶段，`PointUp` 基本都会触发结束路径。

这对一些旧的多指轨迹手势来说可以工作，但对以下手势显然不合适：

- 多指轻点；
- TipTap；
- 固定 N 指 + 另一指快速滑动；
- 未来可能的 anchor + active 类手势。

### 3. 当前手势分类过于粗糙

当前实际上只有两大处理方向：

- 离散轨迹手势；
- 连续手势。

但这不足以覆盖“以接触时序和手指角色关系为核心”的新型多指手势。

---

## 3. 重新定义手势分类

建议将 GestureSign 的手势体系重构为三大类：

## 3.1 轨迹离散手势

特征：

- 用户画出一段轨迹；
- 在手势结束后进行一次性匹配；
- 重点是路径形状，而不是手指之间的相对关系。

示例：

- 两指左滑 / 右滑
- 多指方向手势
- 画线、画圈类轨迹手势

当前对应：

- `PointPattern`
- `GestureManager`

这类手势仍然可以继续使用“特征手指轨迹匹配”作为默认策略。

## 3.2 连续手势

特征：

- 在手指持续接触期间，连续产生输出；
- 每一帧都可能影响结果；
- 重点是运动趋势，而不是最终轨迹形状。

示例：

- 惯性滚动
- 两指缩放
- 连续方向命令

当前对应：

- `ContinuousGestureTrigger`

这类手势本质上不应依赖特征手指，而应基于：

- 质心移动；
- 双指距离变化；
- 多指整体方向一致性；
- 实时速度和趋势。

## 3.3 接触时序手势

特征：

- 重点不是轨迹，而是 down / up 顺序、保持关系、角色划分；
- 常发生在一个较短时间窗口内；
- 可能允许固定手指持续按住并重复触发多个子动作。

示例：

- 多指轻点
- TipTap
- 固定 N 指 + 另一指左/右/上/下滑动
- 未来的 anchor + active 类型手势

这是当前 GestureSign 缺失的一类手势。

---

## 4. 特征手指的重新定位

## 4.1 不再作为全局主模型

在新的架构里，“特征手指”不应该继续作为 GestureSign 的总模型。

系统不应默认认为：

- 只有特征手指才重要；
- 其他手指在进入识别阶段前就可以被丢弃。

## 4.2 作为轨迹手势的一种匹配策略保留

特征手指仍然有价值，但只应保留为：

- 轨迹手势的一种**投影视图**；
- 一种用于降低复杂度的识别策略。

也就是说：

- 输入会话层保留全量触点；
- 轨迹识别器可以选择只消费特征手指轨迹；
- 其他识别器则消费不同视图。

## 4.3 未来可扩展为多种识别视图

建议从设计上引入“识别视图”概念：

- `FeatureFingerView`
- `CentroidView`
- `AllContactsView`
- `RelativeMotionView`
- `ContactTimingView`

这样“特征手指”就不再是系统真相，而只是其中一种识别视图。

---

## 5. 新的总体架构建议

## 5.1 `PointCapture` 继续担任统一编排中心

`PointCapture` 不宜被拆成一个只管轨迹的组件。

更合理的方向是：

- `PointCapture` 继续作为统一手势会话编排中心；
- 在它之下，将不同手势类别交给不同识别器；
- 它负责协调输入生命周期、候选状态、冲突与短路逻辑。

## 5.2 推荐分层

推荐架构如下：

```text
Input Layer
  -> MessageWindow / InputProvider / PointEventTranslator

Session Layer
  -> PointCapture  (统一会话编排中心)

Analysis Layer
  -> GestureAnalysis / GestureSessionViews

Recognizer Layer
  -> TrajectoryGestureRecognizer
  -> ContinuousGestureRecognizer
  -> ContactGestureRecognizer

Execution Layer
  -> GestureManager / PluginManager / ContinuousGestureTrigger / Commands
```

## 5.3 各层职责

### Input Layer

职责：

- 解析原始输入；
- 传递完整触点；
- 不做手势语义判断。

### Session Layer

职责：

- 管理一次手势从开始到结束的全生命周期；
- 维护总手指数、活跃触点集合、时间窗口；
- 负责不同识别器的调度与冲突裁决。

### Analysis Layer

职责：

- 为识别器提供不同视图和基础特征；
- 不直接决定动作；
- 只做轻量预处理和数据组织。

### Recognizer Layer

职责：

- 按手势类别进行专门识别；
- 只消费与自己相关的数据视图；
- 返回候选、命中、失败等结果。

### Execution Layer

职责：

- 根据识别结果触发现有命令执行链路。

---

## 6. GestureSession：建议引入统一会话模型

为了摆脱“边收输入边硬编码判断”的旧模式，建议引入统一的 `GestureSession` 概念。

## 6.1 会话应保留的信息

一个 `GestureSession` 建议至少包含：

- `SourceDevice`
- `StartTime`
- `CurrentTime`
- `ActiveContactIds`
- `MaxFingerCount`
- `CurrentFingerCount`
- `AllContactFrames`
- `SessionState`

以及若干派生视图：

- 特征手指轨迹
- 全量触点轨迹
- 质心轨迹
- 指间距离历史
- 当前活跃 fix fingers / tap finger 信息（未来供 TipTap 使用）

## 6.2 不同手势共享同一会话，不共享同一识别逻辑

重点是：

- 所有手势共享同一输入 session；
- 但不同手势不共享同一种识别算法。

这可以避免：

- 多套状态机争抢输入；
- 不同手势对“开始”和“结束”的定义互相冲突。

---

## 7. 建议引入轻量预处理层，但不要过度设计

## 7.1 预处理的目的不是优化性能，而是做语义分流

预处理层的核心价值，不是“为了少算一点”，而是：

- 先判断这波输入更像哪种结构；
- 再把它交给合适的识别器。

## 7.2 推荐的最小特征集

第一阶段只建议提取少量轻量特征：

- 手指数
- 质心位移
- 每指累计位移
- 方向一致性
- 指间距离变化
- 静止手指数
- 手势持续时间
- 是否 tap-like

## 7.3 推荐的分类输出

预处理层可以先输出这些结构标签：

- `TrajectoryLike`
- `ContinuousLike`
- `ContactTimingLike`
- `RelativeMotionLike`

然后：

- `TrajectoryLike` -> 轨迹识别器
- `ContinuousLike` -> 连续手势识别器
- `ContactTimingLike` -> 接触时序识别器
- `RelativeMotionLike` -> 后续可用于捏合/锚点类手势

## 7.4 不建议一开始做自动最优匹配框架

不建议第一版就做成：

- 任意手势都支持任意策略；
- 系统自动在多种匹配器之间动态切换；
- 每次输入都跑完整全量匹配矩阵。

这会显著增加：

- 架构复杂度；
- 配置复杂度；
- 调试难度。

---

## 8. 旧逻辑优先修复清单

在开发 TipTap 之前，建议先处理以下旧逻辑问题。

## 8.1 修复结束条件模型

当前问题：

- 统一采用“任一手指抬起即结束”逻辑；
- 不适合多指轻点、TipTap 及未来 anchor + active 手势。

建议目标：

- 结束条件不再是全局硬规则；
- 不同手势类别可拥有不同的结束判定；
- `PointCapture` 根据当前候选类型决定结束行为。

## 8.2 保留全量 session 数据

当前问题：

- 进入 `TryBeginCapture()` 后，其他手指的轨迹信息被丢弃；
- 导致新型多指手势无从建模。

建议目标：

- session 层保留全量触点；
- 特征手指轨迹仅作为轨迹识别视图。

## 8.3 把多指轻点从旧逻辑中单独抽出

当前多指轻点与现有多指延迟、粗暴结束逻辑耦合太深。

建议目标：

- 将多指轻点定义为第一个正式的 `ContactGesture`；
- 用它来验证接触时序手势这条新链路。

## 8.4 重新梳理 `MultiFingerDelay` 的职责

当前 `MultiFingerDelay` 更像一个“收手指凑齐后再开始”的临时机制。

建议目标：

- 将它提升为“接触时序手势的早期观察窗口”；
- 在这个窗口中决定：
  - 是多指轻点
  - 是 TipTap 候选
  - 还是进入普通轨迹/连续手势流程。

---

## 9. TipTap 在新架构中的位置

## 9.1 TipTap 不应直接作为当前旧逻辑上的补丁加入

TipTap 不是：

- 一种普通轨迹手势；
- 一种连续手势；
- 一种仅靠特征手指就能表达的手势。

它应当属于：

- `ContactGestureRecognizer` 下的一种专门识别器。

## 9.2 TipTap 的推荐状态模型

未来 TipTap 建议使用：

- `FixSession`
- `TapCycle`

的双层状态模型。

即：

- fix fingers 可以持续按住；
- tap finger 可以多次 down / up；
- 每次 tap 完成后只清当前 tap cycle，而不是清整个 session。

这与当前 GestureSign 的一次性捕获结束模型明显不同，因此必须建立在新的接触时序手势框架之上。

---

## 10. 建议的开发顺序

## 阶段 A：先做架构收敛

- 确认三类手势分类
- 明确 `PointCapture` 的统一编排职责
- 明确特征手指的新定位

## 阶段 B：先修旧逻辑，不新增功能

- 修结束条件
- 保留全量 session 数据
- 梳理 `MultiFingerDelay`

## 阶段 C：抽出接触时序手势框架

- 引入 `ContactGestureRecognizer`
- 先实现多指轻点
- 验证旧手势、连续手势都无回归

## 阶段 D：再开发 TipTap

- 引入 `FixSession + TapCycle`
- 支持连续 TipTap 触发
- 再逐步扩到更多 anchor + active 手势

---

## 11. 结论

当前 GestureSign 的核心问题，已经不再是“输入层拿不到完整多指数据”，而是：

- 现有手势体系仍建立在旧输入缺陷时期的简化假设之上。

因此，接下来的正确方向不是“直接实现 TipTap”，而是：

- 先重构 GestureSign 的手势分层；
- 先修复现有多指手势的旧结束模型和数据保留模型；
- 再把 TipTap 作为新的接触时序手势加入。

一句话总结：

- **连续手势保持独立一类**
- **轨迹手势继续保留特征手指策略**
- **多指轻点与 TipTap 归入新的接触时序手势类别**
- **`PointCapture` 作为统一会话编排中心继续存在，但不再把特征手指当成系统总模型**

