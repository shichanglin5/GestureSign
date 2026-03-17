# 4 指 Click/Tap 手势被误分类为 Trajectory

## 状态: 待修复

## 现象

训练模式下录制 4 指 click（触摸板物理按键）或 4 指 tap 手势时，绝大多数被识别为 Trajectory（轨迹手势）。

诊断日志 `training-session-20260314-163426-*.log` 中 8 个录制块，7 个 Trajectory，仅 1 个 Tap。

## 诊断数据

| 块 | HasPrimaryButtonClick | ActiveContactIds | MaxPerFingerDistance | IsTapLike | 结果 |
|---|---|---|---|---|---|
| 1 | True | [0,1,2] | 45.4 | False | Trajectory |
| 2 | True | [0,1] | 32.4 | False | Trajectory |
| 3 | True | [2] | 38.6 | False | Trajectory |
| 4 | False | [] | 0 | True | **Tap** |
| 5 | False | [1] | 31.0 | False | Trajectory |
| 6 | True | [0,1] | 37.3 | False | Trajectory |
| 7 | True | [0,1,3] | 38.1 | False | Trajectory |
| 8 | True | [0,1] | 28.4 | False | Trajectory |

唯一成功的块 4：所有手指全部释放（ActiveContactIds=[]）、无物理按键、手指完全没移动（MaxPerFingerDistance=0）。

## 根因分析

### 直接原因：会话提前截断

`PointCapture.PointEventTranslator_PointUp`（PointCapture.cs:691）在 `State == CaptureState.Capturing` 时，**第一个手指 up 就触发 `EndCapture()`，不等其余手指释放**：

```csharp
// Case 2: Already capturing - keep legacy behavior for now
if (State == CaptureState.Capturing || (State == CaptureState.CapturingInvalid && allContactsReleased))
{
    _inactivityTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    EndCapture();  // ← 不检查 allContactsReleased
    ...
}
```

对比 Case 1（pending delay 阶段）有明确的等待逻辑：

```csharp
if (_pendingFirstPoints != null)
{
    if (!allContactsReleased)
    {
        e.Handled = Mode != CaptureMode.UserDisabled;
        return;  // ← 等待所有手指释放
    }
    ...
}
```

### 级联影响

1. `EndCapture()` 调用 `CreateSessionSnapshot()`，此时 `_activeContactIds` 非空
2. `MultiFingerClickRecognizer.IsMatch` 第 26 行：`ActiveContactIds.Count > 0 → return false`
3. `MultiFingerTapRecognizer.IsMatch` 第 24 行：`AreAllContactsReleased → false`
4. `IsTapLike = False`（MaxPerFingerDistance 28~45px 超过严格阈值 18px）
5. Click、Tap、TipTap 全部不匹配 → 回退到 Trajectory

### 影响范围

**训练模式和运行时都受影响**。运行时走同样的 `PointEventTranslator_PointUp` → `EndCapture()` 路径。

## 设计矛盾

`Capturing` 状态下收到 up 事件时是否等待所有手指释放，存在 tradeoff：

- **轨迹手势**：一个手指抬起就应该结束——用户画完抬笔即结束
- **Click/Tap 手势**：需要所有手指释放才算完成——4 指同时按下/抬起在物理上不可能完全同步

当前代码注释 `// Case 2: Already capturing - keep legacy behavior for now` 暗示这是已知的遗留行为。

## 可能的修复方向

1. **在 Capturing 状态的 up 事件中增加等待**：检测到 PrimaryButtonClick 时等所有手指释放再 EndCapture
2. **短延迟窗口**：第一个手指 up 后启动短延迟（如 50ms），在窗口内等待其余手指释放
3. **识别器容忍未释放手指**：放宽 Click/Tap 识别器的 ActiveContactIds 检查（但会与 TipTap 混淆）

方向 1 和 2 影响轨迹手势的结束时机，需要评估对现有手势体验的影响。

## 相关文件

- `GestureSign.Daemon/Input/PointCapture.cs:691-745` — PointUp 处理，Case 2 不等待所有手指释放
- `GestureSign.Daemon/Input/MultiFingerClickRecognizer.cs:26` — ActiveContactIds 非空则不匹配
- `GestureSign.Daemon/Input/MultiFingerTapRecognizer.cs:24` — AreAllContactsReleased 检查
- `GestureSign.Daemon/Input/GestureClassifier.cs:11-26` — 训练模式分类逻辑
- `GestureSign.Daemon/Input/GestureAnalyzer.cs:68-90` — IsTapLike 判定
- `GestureSign.Tests/TestCases/TrainingDiagnostics/four-finger-tap-click.log` — 诊断日志

## 复现条件

1. 启动训练模式
2. 用 4 指触摸触摸板，按下物理按键后松开
3. 观察识别结果：绝大多数为 Trajectory 而非 Click
