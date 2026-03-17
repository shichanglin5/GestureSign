# MultiFingerDelay 不再必要的分析

## MultiFingerDelay 的当前流程

```
手指 down → _pendingFirstPoints = points, 启动 delay timer
  ├─ delay 期间有 move 且超过 TapDistanceThreshold → 取消 timer，StartCapture（轨迹）
  ├─ delay 期间有 up 且所有手指释放 → 取消 timer，StartCapture + EndCapture（tap）
  ├─ delay 期间有新手指 down → 合并到 pending，尝试 TipTap promote
  └─ delay 超时 → 尝试 TipTap session，否则 StartCapture
```

## 为什么每种手势都不需要 delay

### 1. 轨迹手势（如 3 指滑动）

用户先落下 3 根手指（落下前基本静止），然后才开始滑动。

- **当前**：delay 期间收集手指，move 超阈值后 StartCapture
- **无 delay**：第一个手指 down 直接进 CapturingInvalid，后续手指 down 时 `_peakFingerCount` 累积。move 超阈值后 CapturingInvalid → Capturing。**行为等价**。

连续手势（如两指缩放）同理：手指逐个落下，在 CapturingInvalid 中累积，move 超阈值后进入 Capturing。

### 2. Tap 手势

Tap 有独立的时长阈值（`TapGestureRecognition.MaxDurationMs`），在手指全部释放时计算 duration 判定。

- **当前**：delay 期间手指 up + 全部释放 → StartCapture + EndCapture → 判定 tap
- **无 delay**：直接在 CapturingInvalid 中，手指 up + 全部释放 → EndCapture → 判定 tap。**行为等价**。

### 3. Click 手势

Click 有独立的按键时长阈值（`ClickGestureRecognition.MaxPressDurationMs`），判定条件是 HasPrimaryButtonClick + 全部释放 + 移动量小。

- **当前**：delay 期间手指 up + 全部释放 → 与 Tap 同路径
- **无 delay**：CapturingInvalid 中，up + 全部释放 → EndCapture → 判定 click。**行为等价**。

### 4. TipTap 手势

TipTap 需要识别 fix 手指（静止不动）和 tap 手指（后来落下，快速抬起）。

- **当前**：delay 超时后 TryBeginTipTapSession，或 delay 期间新手指 down 时 TryPromotePendingToTipTap
- **无 delay**：在 PointDown 的 `State == Ready` 时先 TryBeginTipTapSession；在 CapturingInvalid 中新手指 down 时也可 promote。TipTap 的进入时机从"delay 超时"变为"首个手指 down 时"或"新手指 down 时"

## 边缘 case 分析

### 手指不同时落下

4 指 tap 时，4 根手指物理上不可能同时落下。诊断日志显示间隔约 16ms：
```
contact 0: 0ms
contact 1: 7.9ms
contacts 2,3: 16.4ms
```

- **有 delay**：所有手指在 delay 期间收集到 pending，一次性 StartCapture，`_totalFingerCount` 准确
- **无 delay**：第一个手指 down 进 CapturingInvalid（`_peakFingerCount=1`），后续手指 down 时 `_peakFingerCount` 更新到 2、4。最终结果一致。

### TipTap 进入时机

- **有 delay**：2 指先落下（pending），delay 期间第 3 指 down → TryPromotePendingToTipTap 检查 2 指是否满足 fix finger 条件
- **无 delay**：2 指落下进 CapturingInvalid，第 3 指 down 时在 CapturingInvalid 分支中尝试 promote。需要确保 CapturingInvalid 中的 down 处理有 TipTap promote 逻辑

### SurfaceForm 画线

- **有 delay**：tap 手势在 pending 阶段完成，不触发 CaptureStarted，不创建透明窗口
- **无 delay**：tap 也走 TryBeginCapture → CaptureStarted → StartDrawing。但 tap 手指不移动（不超过阈值），`_shouldDraw` 不会开始绘制，`State` 停留在 `CapturingInvalid`（不转为 Capturing），对用户无可见影响。

## 结论

MultiFingerDelay 在 Hybrid 修复后不再必要。所有手势类型在无 delay 的情况下都能正确工作，且消除了 delay 引入的额外延迟和状态复杂度。
