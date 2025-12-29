# TipTap 手势设计文档

## 1. 概述

### 1.1 TipTap 手势定义

TipTap 是一种多指交互手势，其中：
- **固定手指**：N-1 根手指保持按下不动（锚定手指）
- **点击手指**：1 根手指执行快速的按下-抬起动作（类似点击）
- **位置要求**：点击手指必须是最左侧或最右侧的手指

### 1.2 支持的配置

- **手指总数**：2指、3指、4指
- **点击位置**：左侧点击（Left）、右侧点击（Right）
- **手势标识**：格式为 `{FingerCount}-tiptap-{Position}`
  - 示例：`3-tiptap-right` 表示3指右侧点击

### 1.3 触控板硬件限制

**关键限制**：触控板只能提供 **2个完整手指的轨迹数据**，但能报告总手指数（TotalFingerCount）。

这意味着：
- 对于3指手势，只能追踪2个手指的完整轨迹
- 对于4指手势，只能追踪2个手指的完整轨迹
- 但系统知道实际有多少根手指在触控板上

## 2. 架构设计

### 2.1 组件结构

```
TipTap 系统组成：
├── 数据模型 (GestureSign.Common/Applications/)
│   ├── TipTapGesture.cs          - TipTap手势数据模型
│   └── TapPosition (enum)        - 点击位置枚举
├── 检测引擎 (GestureSign.Daemon/Triggers/)
│   └── TipTapTrigger.cs          - TipTap实时检测逻辑
├── UI配置 (GestureSign.ControlPanel/)
│   ├── ActionDialog.xaml         - TipTap配置界面
│   ├── ActionDialog.xaml.cs      - 配置逻辑
│   └── ActionTitleConverter.cs   - UI显示转换
└── 事件流 (GestureSign.Daemon/Input/)
    └── PointCapture.cs           - 手势结束判断修改
```

### 2.2 事件流程

```
硬件输入
    ↓
PointEventTranslator (触摸事件转换)
    ↓
PointCapture (捕获协调)
    ├─→ CaptureStarted ───→ TipTapTrigger.CaptureStarted (初始化追踪)
    ├─→ PointCaptured ────→ TipTapTrigger.PointCaptured (检测tap)
    ├─→ BeforePointsCaptured → TipTapTrigger.BeforePointsCaptured (阻止普通手势)
    ├─→ GestureRecognized ──→ TipTapTrigger.GestureRecognized (标记互斥)
    └─→ CaptureEnded ─────→ TipTapTrigger.CaptureEnded (重置状态)
```

## 3. 核心实现

### 3.1 TipTap检测逻辑 (TipTapTrigger.cs)

#### 3.1.1 手指追踪

```csharp
private class FingerTracker
{
    public Point InitialPosition { get; set; }  // 初始位置
    public long StartTime { get; set; }         // 按下时间
    public double MaxMovement { get; set; }     // 最大移动距离
}
```

#### 3.1.2 检测阈值

```csharp
private const double TAP_MAX_DISTANCE = 30.0;      // tap最大移动距离 (像素)
private const long TAP_MAX_DURATION = 300;         // tap最大持续时间 (毫秒)
private const double ANCHOR_MAX_MOVEMENT = 50.0;   // 锚定手指最大移动 (像素)
```

#### 3.1.3 Tap检测条件

一个有效的TipTap手势必须满足：

1. **时间条件**：手指按下到抬起的时间 ≤ 300ms
2. **移动条件**：点击手指移动距离 ≤ 30px
3. **位置条件**：点击手指是最左或最右的手指
4. **锚定条件**：其他手指移动距离 ≤ 50px
5. **手指数条件**：有配置的TipTap action匹配当前手指数和点击位置

#### 3.1.4 检测流程

```
CaptureStarted (3指按下)
    ↓
初始化3个 FingerTracker
    ↓
PointCaptured (持续追踪)
    ↓
检测到手指数变化: 3 → 2
    ↓
计算被移除手指的:
  - 持续时间
  - 移动距离
  - 位置 (左/右)
    ↓
验证是否满足tap条件
    ↓
验证锚定手指是否稳定
    ↓
✓ TipTap 检测成功
    ↓
设置 _tipTapTriggered = true
    ↓
触发 OnTipTapRecognized 事件
```

### 3.2 与普通手势的互斥机制

#### 3.2.1 互斥标志

```csharp
private bool _tipTapTriggered = false;        // TipTap已触发 (可继续触发)
private bool _normalGestureTriggered = false; // 普通手势已触发 (完全阻止TipTap)
```

#### 3.2.2 互斥规则

**规则1：TipTap触发后阻止普通手势**
- 当TipTap检测成功 → 设置 `_tipTapTriggered = true`
- 在 `BeforePointsCaptured` 事件中检查
- 如果 `_tipTapTriggered == true` → 取消普通手势识别 (`e.Cancel = true`)

**规则2：普通手势触发后阻止TipTap**
- 当普通手势识别成功 → 触发 `GestureRecognized` 事件
- 设置 `_normalGestureTriggered = true`
- 在 `PointCaptured` 中提前返回，不再检测TipTap

**规则3：连续触发支持**
- `_tipTapTriggered` 不阻止后续TipTap检测
- 允许在固定手指不抬起的情况下，多次点击触发多次action
- 每次捕获会话结束时重置标志

#### 3.2.3 时序图

```
场景1: TipTap先触发
-------------------
3指按下 → CaptureStarted
    ↓
1指快速抬起 → PointCaptured
    ↓
TipTap检测成功 → _tipTapTriggered = true
    ↓
BeforePointsCaptured → 检查到 _tipTapTriggered
    ↓
e.Cancel = true → 阻止普通手势识别
    ↓
剩余手指抬起 → CaptureEnded
    ↓
重置标志 → _tipTapTriggered = false

场景2: 普通手势先触发
---------------------
3指按下 → CaptureStarted
    ↓
移动形成手势轨迹 → GestureRecognized
    ↓
_normalGestureTriggered = true
    ↓
后续PointCaptured → 检查到 _normalGestureTriggered
    ↓
return → 不再检测TipTap
    ↓
手指抬起 → CaptureEnded
    ↓
重置标志 → _normalGestureTriggered = false
```

### 3.3 手势结束判断修改 (PointCapture.cs)

#### 3.3.1 原始逻辑问题

**之前的实现**：任何一根手指抬起就结束手势
```csharp
// 原逻辑
if (State == CaptureState.Capturing)
{
    EndCapture();  // 任何手指抬起都结束
}
```

**问题**：当3指TipTap中1指抬起时，立即结束手势，导致：
1. TipTap无法检测（因为手势已结束）
2. 普通手势优先触发

#### 3.3.2 修改后的逻辑

**新逻辑**：只有剩余手指数 < 2 时才结束手势

```csharp
// 修改后的逻辑
int remainingFingers = e.InputPointList?.Count ?? 0;

if (remainingFingers < 2) {
    // 剩余手指少于2个，结束手势
    EndCapture();
} else {
    // 仍有2个以上手指，继续捕获 (允许TipTap检测)
    GestureSign.Common.Log.Logging.LogDebug($"[PointCapture] {remainingFingers} fingers remaining - continuing capture (allowing TipTap detection)");
    return;
}
```

#### 3.3.3 修改位置

1. **Delay期间** (Line 482-512)
   - 文件：`GestureSign.Daemon/Input/PointCapture.cs:482`
   - 场景：多指按下后的初始延迟期

2. **Capturing期间** (Line 516-548)
   - 文件：`GestureSign.Daemon/Input/PointCapture.cs:516`
   - 场景：正在捕获手势轨迹时

#### 3.3.4 设计理由

**为什么是 < 2 而不是 == 0？**

1. **触控板硬件限制**：只能追踪2个手指的完整轨迹
2. **普通手势需求**：至少需要2个手指的轨迹才能识别手势模式
3. **TipTap检测窗口**：
   - 3指 → 1指抬起 → 剩余2指 → 继续捕获 → TipTap检测
   - 4指 → 1指抬起 → 剩余3指 → 继续捕获 → TipTap检测
   - 剩余手指抬到 < 2 → 结束捕获

### 3.4 连续触发机制

#### 3.4.1 设计目标

允许在固定手指不抬起的情况下，通过重复点击tap手指来触发多次action。

**使用场景示例**：
- 3指固定在触控板上
- 右侧手指快速点击一次 → 触发action（如音量+1）
- 右侧手指再次点击 → 再次触发action（音量+1）
- 无需抬起所有手指重新开始

#### 3.4.2 实现细节

**关键点**：`_tipTapTriggered` 不阻止后续TipTap检测

```csharp
// PointCaptured 中的检测逻辑
if (_normalGestureTriggered) {
    return;  // 只有普通手势触发才阻止TipTap
}
// _tipTapTriggered 不会导致 return，允许继续检测

// 检测到新的tap
if (满足tap条件) {
    _tipTapTriggered = true;  // 可以多次设置为true
    OnTipTapRecognized(...);  // 每次都触发
}
```

**流程示例**：
```
3指按下 (初始状态)
    ↓
右指tap #1 → TipTap检测成功 → 触发action #1
    ↓
_tipTapTriggered = true (不阻止后续检测)
    ↓
右指tap #2 → TipTap再次检测成功 → 触发action #2
    ↓
_tipTapTriggered = true (重复设置，无影响)
    ↓
所有手指抬起 → CaptureEnded → 重置标志
```

## 4. UI配置

### 4.1 配置界面 (ActionDialog.xaml)

位置：在"高级选项"区域，HotKey下方

```xml
<!-- TipTap Gesture -->
<TextBlock Text="{localization:LocalisedText ActionDialog.TipTap}" />
<StackPanel Orientation="Horizontal">
    <TextBlock Text="{localization:LocalisedText ActionDialog.TipTapFingerCount}" />
    <ComboBox x:Name="TipTapFingerCountComboBox">
        <ComboBoxItem Content="2" />
        <ComboBoxItem Content="3" />
        <ComboBoxItem Content="4" />
    </ComboBox>
    <TextBlock Text="{localization:LocalisedText ActionDialog.TipTapPosition}" />
    <ComboBox x:Name="TipTapPositionComboBox">
        <ComboBoxItem Content="{localization:LocalisedText ActionDialog.TapLeft}" Tag="Left" />
        <ComboBoxItem Content="{localization:LocalisedText ActionDialog.TapRight}" Tag="Right" />
    </ComboBox>
</StackPanel>
<Button x:Name="ResetTipTapButton" Click="ResetTipTapButton_Click" />
```

### 4.2 配置保存逻辑

```csharp
// 保存TipTap配置
if (TipTapFingerCountComboBox.SelectedIndex >= 0 &&
    TipTapPositionComboBox.SelectedIndex >= 0)
{
    int fingerCount = TipTapFingerCountComboBox.SelectedIndex + 2;
    var tapPosition = (TapPosition)TipTapPositionComboBox.SelectedIndex;

    NewAction.TipTapGesture = new TipTapGesture(fingerCount, tapPosition);

    // 生成虚拟手势名称
    NewAction.GestureName = $"{fingerCount}-tiptap-{tapPosition.ToString().ToLower()}";
}
```

### 4.3 UI显示转换 (ActionTitleConverter.cs)

在action列表中显示TipTap配置：

```csharp
if (action.TipTapGesture != null)
{
    string tapPositionText = action.TipTapGesture.TapPosition == TapPosition.Left
        ? LocalizationProvider.Instance.GetTextValue("ActionDialog.TapLeft")
        : LocalizationProvider.Instance.GetTextValue("ActionDialog.TapRight");

    actionName += "\n" + LocalizationProvider.Instance.GetTextValue("ActionDialog.TipTap") + ": " +
        action.TipTapGesture.FingerCount + "-" + tapPositionText;
}
```

显示示例：
```
动作名称
点按手势: 3-右侧点击
```

## 5. 关键代码位置

### 5.1 数据模型

| 文件 | 位置 | 说明 |
|------|------|------|
| `GestureSign.Common/Applications/TipTapGesture.cs` | 完整文件 | TipTap手势数据模型 |
| `GestureSign.Common/Applications/IAction.cs` | Line 19 | TipTapGesture属性声明 |
| `GestureSign.Common/Applications/Action.cs` | Line 51, 94 | 属性实现和DeepCopy |

### 5.2 检测引擎

| 文件 | 位置 | 说明 |
|------|------|------|
| `GestureSign.Daemon/Triggers/TipTapTrigger.cs` | 完整文件 | TipTap检测核心逻辑 |
| `GestureSign.Daemon/Triggers/TriggerManager.cs` | Line 41 | TipTapTrigger注册 |

### 5.3 手势捕获修改

| 文件 | 位置 | 说明 |
|------|------|------|
| `GestureSign.Daemon/Input/PointCapture.cs` | Line 482-512 | Delay期间的手势结束判断 |
| `GestureSign.Daemon/Input/PointCapture.cs` | Line 516-548 | Capturing期间的手势结束判断 |

### 5.4 UI配置

| 文件 | 位置 | 说明 |
|------|------|------|
| `GestureSign.ControlPanel/Dialogs/ActionDialog.xaml` | Line 92-134 | TipTap配置UI |
| `GestureSign.ControlPanel/Dialogs/ActionDialog.xaml.cs` | Line 85-90 | 加载配置 |
| `GestureSign.ControlPanel/Dialogs/ActionDialog.xaml.cs` | Line 172-183 | 保存配置 |
| `GestureSign.ControlPanel/Dialogs/ActionDialog.xaml.cs` | Line 115-119 | 重置按钮 |
| `GestureSign.ControlPanel/Converters/ActionTitleConverter.cs` | Line 40-47 | UI显示转换 |

### 5.5 语言资源

| 文件 | 位置 | 说明 |
|------|------|------|
| `GestureSign.ControlPanel/Languages/ControlPanel/zh.xml` | Line 116-120 | 中文资源 |
| `GestureSign.ControlPanel/Languages/ControlPanel/en.xml` | Line 112-116 | 英文资源 |

## 6. 调试和日志

### 6.1 关键日志点

TipTapTrigger日志前缀：`[TipTapTrigger]`

**初始化阶段**：
```
[TipTapTrigger] CaptureStarted - FingerCount=3, actionsWithTipTap.Count=1
[TipTapTrigger] CaptureStarted - Initialized 3 trackers
```

**检测阶段**：
```
[TipTapTrigger] Finger added: 2 -> 3
[TipTapTrigger] Finger removed: 3 -> 2, checking for tap...
[TipTapTrigger] Removed finger 2: duration=150ms, movement=15.5px
[TipTapTrigger] Tap position determined: Right
[TipTapTrigger] Anchor fingers stable: true
[TipTapTrigger] TipTap detected: 3 fingers, Right tap at (800, 400)
```

**互斥阶段**：
```
[TipTapTrigger] Cancelling normal gesture recognition because TipTap was already triggered
[TipTapTrigger] Normal gesture 'gesture_name' recognized, blocking TipTap detection
```

**失败原因**：
```
[TipTapTrigger] Not a tap: duration 450ms > 300ms OR movement 45.0px > 30.0px
[TipTapTrigger] Tap position determined: null
[TipTapTrigger] Anchor fingers stable: false
```

PointCapture日志前缀：`[PointCapture]`

**手势结束判断**：
```
[PointCapture] Finger lifted during capture - remainingFingers: 2
[PointCapture] 2 fingers remaining - continuing capture (allowing TipTap detection)
[PointCapture] Finger lifted during capture - remainingFingers: 1
[PointCapture] Ending gesture - remainingFingers=1
```

### 6.2 调试建议

1. **验证TipTap action是否配置**
   ```
   [TipTapTrigger] CaptureStarted - actionsWithTipTap.Count=0
   ```
   如果count=0，说明没有配置TipTap action

2. **检查tap检测条件**
   - duration：是否 ≤ 300ms
   - movement：是否 ≤ 30px
   - position：是否是leftmost/rightmost
   - anchors：其他手指是否稳定 (≤ 50px)

3. **验证手势结束时机**
   ```
   [PointCapture] X fingers remaining - continuing capture
   ```
   应该在剩余 ≥2 指时继续捕获

4. **检查互斥机制**
   - TipTap触发后应该看到 "Cancelling normal gesture"
   - 普通手势触发后应该看到 "blocking TipTap detection"

## 7. 已知限制和注意事项

### 7.1 硬件限制

1. **触控板只能追踪2个手指的完整轨迹**
   - 3指/4指手势只能获得2个手指的详细位置
   - 但能知道总手指数（TotalFingerCount）

2. **手指位置判断的准确性**
   - 依赖于前2个手指的位置报告
   - 可能存在误判（特别是4指TipTap）

### 7.2 设计权衡

1. **手势结束判断：< 2 vs == 0**
   - 选择 `< 2`：因为触控板限制，至少需要2个手指轨迹
   - 可能导致卡住：如果最后PointUp事件的remainingFingers仍≥2
   - 缓解方案：依赖InactivityTimeout (100ms)自动清理

2. **TipTap检测优先级**
   - TipTap和普通手势是平等竞争关系
   - 哪个先检测到，哪个就阻止另一个
   - 不能保证TipTap一定优先（取决于用户操作速度）

3. **连续触发的性能**
   - 每次tap都会触发完整的action执行
   - 高频率连续点击可能导致action队列堆积
   - 建议在action中添加防抖逻辑

### 7.3 使用建议

1. **合理的tap阈值**
   - 当前：300ms / 30px
   - 可根据实际使用体验调整
   - 过严格：难以触发
   - 过宽松：误触发增加

2. **手势设计**
   - 优先使用3指TipTap（2个完整轨迹 + 1个tap）
   - 4指TipTap可能不稳定（只有2个轨迹，难以准确判断哪个是tap手指）

3. **Action配置**
   - 避免同时配置冲突的手势
   - 例如：3指左滑 + 3指左侧TipTap 可能冲突
   - 建议分开配置到不同应用

## 8. 未来改进方向

### 8.1 短期优化

1. **可配置的检测阈值**
   - 在UI中暴露 TAP_MAX_DURATION、TAP_MAX_DISTANCE
   - 允许用户根据习惯调整

2. **更智能的位置判断**
   - 考虑手指的相对位置而非绝对位置
   - 使用机器学习识别tap模式

3. **增强的日志和诊断**
   - 提供TipTap检测失败的详细原因
   - 可视化显示每个手指的轨迹和检测结果

### 8.2 长期规划

1. **支持更多tap模式**
   - 双击（double tap）
   - 长按tap（long tap）
   - 多指同时tap

2. **手势优先级系统**
   - 允许配置TipTap优先级高于普通手势
   - 或反之

3. **自适应学习**
   - 根据用户的使用习惯自动调整阈值
   - 记录和分析失败的tap尝试

## 9. 测试指南

### 9.1 基本功能测试

**测试1：单次TipTap触发**
1. 配置：3指-右侧点击 → 打开记事本
2. 操作：3指按下，右指快速抬起再放下
3. 期望：记事本打开，日志显示 "TipTap detected: 3 fingers, Right tap"

**测试2：连续TipTap触发**
1. 配置：3指-右侧点击 → 音量+5
2. 操作：3指按下，右指连续快速点击3次
3. 期望：音量增加15 (3次×5)

**测试3：TipTap vs 普通手势互斥**
1. 配置：3指-右侧点击 + 3指右滑手势
2. 操作：3指按下，右指快速tap
3. 期望：只触发TipTap，不触发右滑

### 9.2 边界条件测试

**测试4：tap时间超限**
1. 操作：手指按下500ms后抬起
2. 期望：不触发TipTap，日志显示 "duration 500ms > 300ms"

**测试5：tap移动超限**
1. 操作：手指移动50px后抬起
2. 期望：不触发TipTap，日志显示 "movement 50.0px > 30.0px"

**测试6：锚定手指移动**
1. 操作：tap的同时，其他手指也移动60px
2. 期望：不触发TipTap，日志显示 "Anchor fingers stable: false"

### 9.3 压力测试

**测试7：快速连续tap**
1. 操作：1秒内点击10次
2. 期望：所有tap都能被识别，action正常执行

**测试8：手势结束判断**
1. 操作：所有手指快速同时抬起
2. 期望：手势正常结束，不卡住

## 10. 变更历史

| 日期 | 版本 | 变更内容 |
|------|------|----------|
| 2025-12-30 | 1.0 | 初始设计和实现 |
|  |  | - 创建TipTapGesture数据模型 |
|  |  | - 实现TipTapTrigger检测逻辑 |
|  |  | - 添加UI配置支持 |
|  |  | - 修改PointCapture手势结束判断 |
|  |  | - 实现与普通手势的互斥机制 |
|  |  | - 支持连续触发 |

---

**维护者**：GestureSign Team
**最后更新**：2025-12-30
**文档版本**：1.0
