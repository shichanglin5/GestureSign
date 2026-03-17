# _totalFingerCount 冗余分析

## 当前的数据来源

### `e.TotalFingerCount`（来自 HID）

`InputPointsEventArgs.TotalFingerCount` 来自 HID 驱动报告的 `OriginalContactCount`（MessageWindow 中解析的 contactCount 字段）。

构造函数：
```csharp
TotalFingerCount = totalFingerCount > 0 ? totalFingerCount : inputPointList?.Count ?? 0;
```

Hybrid 修复后，`OriginalContactCount` 与 `InputPointList.Count` 一致（每帧数据完整），所以这个值等于 `InputPointList.Count`。

### `_totalFingerCount`（PointCapture 内）

通过 `Math.Max` 在多处累积：
```csharp
// PointDown 中（已在 Capturing）
_totalFingerCount = Math.Max(_totalFingerCount, e.TotalFingerCount);

// TryBeginCapture 中
_totalFingerCount = totalFingerCount;

// TipTap 中
_totalFingerCount = Math.Max(_totalFingerCount, e.TotalFingerCount);
```

### `_activeContactIds`（实时触点集合）

`TrackAllPoints` 中维护：
```csharp
// state != None → _activeContactIds.Add(id)
// state == None → _activeContactIds.Remove(id)
```

实时反映当前在板上的触点数量。

## 为什么 `_totalFingerCount` 是冗余的

| 信息 | _totalFingerCount | _activeContactIds |
|---|---|---|
| 当前手指数 | 不准确（只增不减） | 准确（实时维护） |
| 历史最大手指数 | 是（Math.Max 累积） | 需要额外字段追踪 |
| 数据来源 | e.TotalFingerCount（HID） | TrackAllPoints（实际触点） |

`_totalFingerCount` 的唯一价值是"历史最大手指数"。这个信息可以用一个简单的 `_peakFingerCount` 字段在 `TrackAllPoints` 中维护：

```csharp
_peakFingerCount = Math.Max(_peakFingerCount, _activeContactIds.Count);
```

## 使用场景审计

`_totalFingerCount` 在以下场景使用：

1. **TryBeginCapture** — 初始化时设置 `_totalFingerCount = totalFingerCount`
   → 改为 `_peakFingerCount = _activeContactIds.Count`

2. **PointDown 已在 Capturing** — `_totalFingerCount = Math.Max(_totalFingerCount, e.TotalFingerCount)`
   → 由 TrackAllPoints 中的 `_peakFingerCount` 自动追踪

3. **SurfaceForm.UpdateFingerCount** — 通知画线控件手指数增加
   → 改用 `_peakFingerCount`

4. **EndCapture → RecordedGestureSample.FingerCount** — 用于手势分类
   → 改用 `_peakFingerCount`

5. **HandleEndOfPoints → PointsCapturedEventArgs.FingerCount** — 用于手势匹配
   → 改用 `_peakFingerCount`

6. **GetCurrentEstimatedFingerCount** — 返回 max(activePoints, activeContactCount, _pendingTotalFingerCount, _totalFingerCount)
   → 去掉 _pendingTotalFingerCount，简化为 max(_activeContactIds.Count, _peakFingerCount)

## 结论

`_totalFingerCount` 可以用 `_peakFingerCount`（在 `TrackAllPoints` 中通过 `_activeContactIds.Count` 追踪的历史最大值）完全替代。同时移除 `_pendingTotalFingerCount`（随 delay 一起去掉）。
