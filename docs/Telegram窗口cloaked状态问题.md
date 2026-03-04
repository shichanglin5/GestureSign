# Telegram 窗口 DWM Cloaked 状态问题

## 1. 问题现象

使用 ActivateApp 激活 Telegram 时，窗口有时无法正常显示：

- 日志显示 `TryActivateWindow -> True`，前台窗口句柄正确
- 但窗口实际不可见、不可交互
- 任务栏缩略图正常显示，但点击缩略图也无法恢复窗口
- 需要多次点击任务栏图标才能恢复

## 2. 调查过程

### 2.1 辅助窗口误匹配

Telegram（基于 Qt 5.15）存在多个顶层窗口：

| 窗口标题 | 类型 | Win32 属性 |
| ---- | ---- | ---- |
| `Telegram` | 主窗口 | class=Qt51518QWindowIcon, owner=0, 无 cloaked |
| `TelegramDesktop` | 辅助窗口 | class=Qt51518QWindowOwnDCIcon, owner=0, 无 cloaked |

两者在 Win32 层面几乎无法区分（相同的 exStyle、owner=0、非 cloaked、正常尺寸）。
唯一的结构性差异是 `WS_MAXIMIZE` 位（当前状态，非结构属性）。

**解决方案**：Title 匹配从 Contains（包含）改为 Equals（精确匹配），`Title=Telegram` 不再匹配 `TelegramDesktop`。

### 2.2 Cloaked 状态导致窗口不可见

通过添加诊断日志发现，Telegram 主窗口在某些操作后会进入 DWM cloaked 状态（`DWMWA_CLOAKED=1`，即 `DWM_CLOAKED_APP`）。

典型时间线：

1. 第一次激活：窗口可见（isVisible=True, isCloaked=False），正常成功
2. 第二次激活：窗口不可见（isVisible=False），通过 showHidden 路径恢复成功
3. 第三次激活：窗口 cloaked（isVisible=True, isCloaked=True），被 `IsSwitchableWindow` 过滤

cloaked 窗口的特殊之处：
- `IsWindowVisible()` 返回 `true`（Win32 层面可见）
- `DWMWA_CLOAKED != 0`（DWM 层面隐藏）
- 窗口实际不可见、不可交互

### 2.3 尝试解除 Cloaked

| 方案 | 结果 |
| ---- | ---- |
| `DwmSetWindowAttribute(DWMWA_CLOAK=FALSE)` | 无效，第三方进程无法 uncloake 其他应用的窗口 |
| `ShowWindow(SW_RESTORE)` | 无效，cloaked 状态不受 ShowWindow 影响 |
| 启动新实例让 Telegram 自行恢复 | 任务栏闪烁但主窗口仍为 cloaked |

搜索 Microsoft 文档确认：`DwmSetWindowAttribute` 虽然接受任意 HWND，但没有文档或实例证明第三方进程能成功 uncloake 其他应用 cloaked 的窗口。`DWM_CLOAKED_APP` 表示窗口被其所属应用自行 cloaked，恢复也需要应用自身触发。

## 3. 最终处理方案

### 3.1 扫描阶段：接受 cloaked 窗口进入 hidden 扫描

`IsActivatableHiddenWindow` 修改为同时接受不可见窗口和 cloaked 窗口：

```csharp
bool isVisible = IsWindowVisible(hWnd);
bool isCloaked = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, ...) == 0 && cloaked != 0;

if (isVisible && !isCloaked)
    return false;  // 正常可见窗口不在 hidden 扫描范围
```

### 3.2 激活阶段：cloaked 窗口走 showHidden 路径

`HandleSingleWindow` 中 `!isVisible || isCloaked` 统一进入 showHidden 分支。虽然 showHidden 对 cloaked 窗口实际无法恢复显示，但保持统一的代码路径，避免走到启动路径重复启动应用。

### 3.3 TryActivateWindow 的 showHidden 分支

仅处理 Win32 层面不可见的窗口（`IsWindowVisible=false`），不处理 cloaked。
注释中记录了 cloaked 窗口的已知限制。

## 4. 已知限制

- **cloaked 窗口无法通过外部 API 恢复显示**：这是 Qt 框架与 DWM 的交互方式决定的，与微信托盘恢复卡死属于同一类问题（Qt 内部状态机控制）
- **Telegram 特有**：并非所有 Qt 应用都会进入 cloaked 状态，这取决于应用自身的窗口管理逻辑
- **用户可通过点击任务栏图标恢复**：多次点击可触发 Telegram 自身的恢复管线

## 5. 相关改动

### 5.1 Title 匹配改为精确匹配

**文件**: `GestureSign.Common/Applications/WindowMatcher.cs`

`MatchTitle` 和 `MatchTitleValue` 方法中，非正则模式从 `Contains` 改为 `string.Equals`：

```csharp
// 改动前
return windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase);

// 改动后
return string.Equals(windowTitle, pattern, StringComparison.OrdinalIgnoreCase);
```

如需包含匹配，用户可勾选正则模式。

### 5.2 ForceSetForegroundWindow 简化

**文件**: `ManagedWinapi/Windows/SystemWindow.cs`

移除了以下无效或不必要的激活手段：

- `mouse_event(MOUSEEVENTF_MOVE)` 零位移鼠标注入
- `BringWindowToTop` 兜底
- `DwmSetWindowAttribute(DWMWA_CLOAK=FALSE)` uncloake 尝试

`ForceSetForegroundWindow` 简化为单次 `SetForegroundWindow` 调用。
对于 UIAccess 进程，`SetForegroundWindow` 不受前台锁限制，单次调用足够。
