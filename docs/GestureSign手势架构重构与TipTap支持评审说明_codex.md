# GestureSign 手势架构重构与 TipTap 支持评审说明

## 1. 背景

这次重构的起点是两个历史前提发生了变化：

1. 过去 GestureSign 在触控板多指输入解析上存在缺陷，导致：
   - 多指触点数据看起来不完整；
   - 手势结束阶段未必能稳定消费到所有手指抬起事件；
   - 为了让系统能工作，识别层大量依赖“特征手指”与“任一手指抬起即结束”这类简化假设。

2. 在修复 HID LinkCollection / Hybrid 报告解析后，当前目标设备上已经验证：
   - 2/3/4/5 指触点可完整上送；
   - 可获得独立的 ContactId、坐标和手指数；
   - 之前很多“平台限制”其实是 GestureSign 自身旧输入解析造成的假象。

因此，这次工作不只是“新增 TipTap”，而是对整个手势体系做一次重构：

- 连续手势收敛为真正高频使用的两指滑动/缩放；
- 非连续手势统一走录制创建；
- 非连续手势在录制后自动分类为轨迹 / Tap / TipTap；
- 所有手势逐步收敛为“全局定义 + 应用绑定”模型；
- 特征手指从“系统总模型”降级为“轨迹匹配内部策略”。

---

## 2. 目标

本次改动的目标分为四个层次：

### 2.1 产品目标

- 连续手势只保留：
  - 两指滑动
  - 两指缩放
- 非连续手势统一通过录制创建
- 录制完成后自动识别手势类型
- 在动作列表中展示清晰的手势类型标签

### 2.2 架构目标

- 建立完整的多指 session 数据保留层
- 引入轻量 `GestureAnalysis` / `GestureSessionSnapshot`
- 建立 `Tap` / `TipTap` 的接触时序手势能力
- 让轨迹手势支持稳定 `GestureId`
- 让应用动作优先按 `GestureId` 绑定，而不是只靠 `GestureName`

### 2.3 交互目标

- 维持原有“点击 `+` 后录制手势”的主要交互路径
- 不要求用户先理解轨迹 / Tap / TipTap 的分类
- 系统录制后自动分类并展示结果

### 2.4 性能目标

- 通过分类分流避免所有手势都进入轨迹匹配
- 为轨迹手势建立候选索引缓存
- 缓存模板 `PointsPatternSet`，减少匹配期重复构造

---

## 3. 核心设计决策

## 3.1 手势分层

手势最终分成两大产品层类别：

### 连续手势

只保留：
- 两指滑动
- 两指缩放

不再保留：
- 3/4/5 指连续滚动
- 连续方向命令
- 任意手指数连续手势配置

### 非连续手势

统一通过录制创建，并自动分类为：
- 轨迹手势
- 多指轻点（Tap）
- TipTap
- 后续其他接触时序手势

---

## 3.2 特征手指的定位变化

旧模型中，特征手指几乎等于“系统唯一有效轨迹”。

新模型中：
- 原始 session 永远保留全量多指数据；
- 特征手指仅作为轨迹手势内部的一种匹配策略；
- 不允许特征手指污染或替代完整原始数据。

这也是本次重构最重要的边界之一。

---

## 3.3 全局手势定义 + 应用绑定

轨迹手势已经具备全局复用基础，因此本次重构把这个方向继续推进：

- 手势定义具备稳定 `Id`
- 应用动作优先通过 `GestureId` 绑定手势
- `GestureName` 保留为兼容字段与展示名称
- 修改已有轨迹手势时，按 `GestureId` 重绑所有引用

对于 Tap / TipTap：
- 当前已经具备全局定义对象（`TapGestureConfig` / `TipTapGestureConfig`）
- 并能通过录制结果自动复用已有定义
- 这为后续彻底统一到全局手势库打下基础

---

## 3.4 录制后自动分类

训练模式不再只向控制面板发送 `PointPattern[]`，而是支持发送 richer result：

- `RecordedGestureSample`
- `GestureAnalysis`
- `GestureSessionSnapshot`
- `RecordedGestureDefinitionResult`

录制后流程变成：

```text
手势录制
 -> RecordedGestureSample
 -> GestureClassifier
 -> RecordedGestureDefinitionResult
 -> 控制面板展示 / 保存
```

这使得 GestureSign 可以在录制结束后自动判断：
- 轨迹
- Tap
- TipTap

而不是默认一律按轨迹手势处理。

---

## 3.5 连续手势简化

连续手势主界面收敛为：
- 继承
- 启用
- 禁用

对象为：
- 两指滑动
- 两指缩放

同时保留高级设置对话框用于：
- 滚动方向
- 惯性滚动参数
- 缩放速度
- 缩放灵敏度

但彻底移除了旧的：
- 自定义连续方向命令
- 3/4 指连续配置

---

## 4. 主要代码改动

以下按模块列出主要变化。

## 4.1 输入与会话层

### `GestureSign.Daemon/Input/PointCapture.cs`

主要变化：
- 增加全量多指 session 数据保留：
  - `_allPointsCaptured`
  - `_activeContactIds`
- 增加 session 生命周期信息：
  - `_captureStartedAt`
- 补充 session helper：
  - `TrackAllPoints(...)`
  - `CreateSessionSnapshot()`
  - `ResetSessionTracking()`
- 让等待窗口不再简单地“任一手指抬起即结束全部逻辑”
- 在训练模式下生成 `RecordedGestureDefinitionResult`
- 接入 `Tap` / `TipTap` 后端识别与执行链

### `GestureSign.Daemon/Input/InputPoint.cs`
### `GestureSign.Daemon/Input/InputPointsEventArgs.cs`

主要变化：
- `InputPoint` 增加 `DeviceStates State`
- 原始 `RawData.State` 能一路透传到捕获层

---

## 4.2 分析与分类层

### `GestureSign.Common/Input/GestureAnalysis.cs`
### `GestureSign.Common/Input/GestureSessionSnapshot.cs`
### `GestureSign.Common/Input/RecordedGestureSample.cs`

新增内容：
- 轻量分析结构
- 会话快照结构
- 录制样本结构

### `GestureSign.Daemon/Input/GestureAnalyzer.cs`
### `GestureSign.Daemon/Input/GestureClassifier.cs`
### `GestureSign.Daemon/Input/GestureDefinitionFactory.cs`

新增内容：
- 手势结构分析
- 轨迹 / Tap / TipTap 分类
- 录制后定义结果生成
- TipTap 方向推断（Left / Right / Middle）
- 对已有全局 Tap/TipTap 定义的自动复用

---

## 4.3 Tap / TipTap 配置与识别

### `GestureSign.Common/Applications/ContactGestureConfig.cs`

新增：
- `ContactGestureSettings`
- `TapGestureConfig`
- `TipTapGestureConfig`
- `TapGestureRecognition`
- `TipTapRecognition`
- `ContactGestureDirection`

### `GestureSign.Daemon/Input/MultiFingerTapRecognizer.cs`
### `GestureSign.Daemon/Input/TipTapRecognizer.cs`
### `GestureSign.Daemon/Input/TipTapRuntimeState.cs`

新增：
- 多指轻点识别器
- TipTap 基础识别器
- TipTap 持续 session / tap cycle 运行时状态

### `GestureSign.Common/Applications/ApplicationManager.cs`

新增：
- `GetRecognizedTapCommands(...)`
- `GetRecognizedTipTapConfigs(...)`
- `GetGlobalTapDefinitions(...)`
- `GetGlobalTipTapDefinitions(...)`

作用：
- 支持 contact gesture 的全局/应用级查找与执行

---

## 4.4 轨迹手势全局定义与复用

### `GestureSign.Common/Gestures/IGesture.cs`
### `GestureSign.Common/Gestures/Gesture.cs`

新增：
- `Id`
- `DisplayPointPatterns`

### `GestureSign.Common/Applications/IAction.cs`
### `GestureSign.Common/Applications/Action.cs`

新增：
- `GestureId`

### `GestureSign.Common/Input/RecognitionEventArgs.cs`

新增：
- `GestureId`

### `GestureSign.Common/Gestures/GestureManager.cs`

主要变化：
- 增加 `GestureId` 主链
- `GetGestureById(...)`
- `DeleteGestureById(...)`
- `GetNewGestureId(...)`
- 缺失 `Id` 的旧手势在加载时自动补 `Id`
- 建立候选索引缓存：
  - `(FingerCount, TrajectoryCount, Level)`
- 建立模板 `PointsPatternSet` 缓存
- 轨迹匹配时优先使用缓存

### `GestureSign.Common/Extensions/ApplicationExtensions.cs`

新增：
- `RebindGestures(...)`

作用：
- 修改已有轨迹手势时，所有引用按 `GestureId` 自动重绑

---

## 4.5 连续手势简化

### `GestureSign.Common/Applications/TwoFingerGestureSettings.cs`

新增：
- `InheritSwitch`
- `TwoFingerGestureSettings`
- `TwoFingerZoomSettings`

### `GestureSign.Common/Applications/IApplication.cs`
### `GestureSign.Common/Applications/ApplicationBase.cs`

新增：
- `TwoFingerGestures`

### `GestureSign.Daemon/Triggers/ContinuousGestureTrigger.cs`

主要变化：
- 连续手势运行时只处理两指
- 通过 `TwoFingerGestures` 读取配置
- 去掉 3/4 指连续路径
- 去掉 `Custom` 连续方向命令运行逻辑

### `GestureSign.ControlPanel/MainWindowControls/AvailableActions.cs`
### `GestureSign.ControlPanel/Dialogs/ContinuousGestureConfigDialog.xaml(.cs)`

主要变化：
- 控制面板主界面连续手势收敛为：
  - 两指滑动
  - 两指缩放
  - 三态：继承 / 启用 / 禁用
- 高级配置对话框只保留两指滚动/缩放相关参数
- 移除 `Custom` 方向命令编辑逻辑

---

## 4.6 控制面板录制与展示

### `GestureSign.ControlPanel/MessageProcessor.cs`

新增：
- `GotNewGestureDefinition`

作用：
- 控制面板可以接收分类后的录制结果对象

### `GestureSign.ControlPanel/UserControls/GestureSelector.xaml(.cs)`

主要变化：
- 增加 `CurrentRecordedDefinition`
- 录制后可显示：
  - 轨迹手势
  - Tap
  - TipTap
- 显示基础类型标签
- 训练模式能接收分类后的定义对象

### `GestureSign.ControlPanel/Common/ContactGestureDisplayFactory.cs`

新增：
- Tap / TipTap 的基础语义示意图构造

### `GestureSign.ControlPanel/Converters/GestureImageConverter.cs`
### `GestureSign.ControlPanel/ViewModel/GestureItemProvider.cs`
### `GestureSign.ControlPanel/UserControls/ApplicationSelector.xaml.cs`

主要变化：
- 轨迹手势显示优先使用 `DisplayPointPatterns`
- 为“全手指轨迹显示”打通显示链路

### `GestureSign.ControlPanel/Dialogs/ActionDialog.xaml(.cs)`

主要变化：
- 支持 `CurrentRecordedDefinition`
- 若录制结果为 Tap / TipTap，则保存对应全局定义对象，并绑定 `GestureId`
- 若录制结果为轨迹，则继续保存轨迹手势

### `GestureSign.ControlPanel/Dialogs/GestureDefinition.xaml.cs`

主要变化：
- 编辑已有轨迹手势时，会提示“修改后影响所有引用”
- 保存时优先按 `GestureId` 替换定义并重绑引用

### `GestureSign.ControlPanel/Converters/ActionTitleConverter.cs`

主要变化：
- 动作标题基础版支持 `[Tap]` / `[TipTap]` 标签显示

---

## 5. 测试与验证

新增测试：

- `GestureSign.Tests/ContactGestureConfigTests.cs`
- `GestureSign.Tests/ContactRecognizerTests.cs`
- `GestureSign.Tests/TwoFingerGestureSettingsTests.cs`
- `GestureSign.Tests/GestureIdBindingTests.cs`
- `GestureSign.Tests/GestureClassifierTests.cs`

当前测试结果：
- `32/32` 通过

构建结果：
- `dotnet build GestureSign.sln -c Debug` 通过

---

## 6. 结果总结

这次重构完成后，GestureSign 的核心变化可以概括为：

1. **连续手势从“泛化多指连续命令系统”收敛为“两指滑动 / 两指缩放”**
2. **非连续手势统一走录制创建，并在录制完成后自动分类**
3. **轨迹手势、Tap、TipTap 都开始进入“全局定义 + 应用绑定”的方向**
4. **原始多指数据不再被特征手指模型污染，特征手指只保留为内部轨迹匹配策略**
5. **轨迹匹配增加索引和模板缓存，性能路径更清晰**

---

## 7. 当前评审重点

建议 review 时重点关注：

- `PointCapture` 中全量 session 与 contact gesture 链路是否合理
- `GestureClassifier` / `GestureDefinitionFactory` 的分类与定义生成是否足够稳妥
- `GestureId` 主链是否已经覆盖关键路径
- `TwoFingerGestures` 是否足够替代旧连续手势模型
- 控制面板录制链是否符合既有用户习惯
- 是否还存在残留旧逻辑与命名不一致的地方

---

## 8. 已知边界

当前实现已经达到“当前路线文档定义的完成态”，但仍有一些后续可增强点：

- “命中已有全局手势时”的完整三选一交互（复用 / 修改 / 另存为新）目前是基础版复用与提示
- Tap / TipTap 的语义图目前是基础版，并非最终精细视觉稿
- ContactGesture 全局定义库虽然已具备基础结构，但还没有像轨迹手势那样完全统一到单一管理入口

这些不影响当前重构的主目标完成，但可作为下一轮增强方向。

