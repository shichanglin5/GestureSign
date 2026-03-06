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

### 3.4 Cloaked owner 导致新窗口被过滤

日期：2026-03-04

Telegram 关闭主窗口后重新打开，新的主窗口 owner 指向旧的 cloaked 窗口。由于旧窗口 `IsWindowVisible=True`（DWM cloaked 但 Win32 层面可见），owner 可见性检查会将新窗口视为"有可见 owner 的子窗口"而过滤。

**修复**：在 owner 可见性判定中增加 DWM cloaked 检查。当 owner `IsWindowVisible=True` 但 `DWMWA_CLOAKED != 0` 时，视为不可见，放行 owned 窗口。

### 3.5 Ghost 窗口 `TelegramDesktop`

日期：2026-03-05

Telegram 关闭主窗口后，`TelegramDesktop` 窗口持续存在：

- `IsWindowVisible=True`，`DWMWA_CLOAKED=0`（非 cloaked），窗口矩形正常大小
- Win32 属性层面与真实窗口无法区分
- 但实际不可交互、不可激活

该窗口是 Qt 框架内部管理的辅助窗口，关闭主窗口后未被销毁。

**修复**：将 `TelegramDesktop` 加入 `WindowTitleBlacklist`。这是最简单有效的方案，因为该窗口在 Win32 层面与真实窗口无结构性差异。

### 3.6 无可激活窗口时的启动行为

关闭 Telegram 主窗口后，可能只剩 ghost 窗口和 helper 窗口（均被过滤），导致 `appWindows.Count == 0`。此时直接进入 `TryLaunchApplication` 启动新实例。

对于 Telegram 这样的单实例应用，重复启动会通过 IPC 通知已有进程恢复窗口（而非创建新实例），这正是我们需要的效果。

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

### 5.3 Owner cloaked 检查

**文件**: `GestureSign.CorePlugins/ActivateApp/ActivateAppPlugin.cs`

`IsSwitchableWindow` 和 `IsActivatableHiddenWindow` 中的 owner 可见性判定增加 DWM cloaked 检查：

```csharp
if (ownerWindow != IntPtr.Zero && IsWindowVisible(ownerWindow))
{
    if (DwmGetWindowAttribute(ownerWindow, DWMWA_CLOAKED, out int ownerCloaked, ...) == 0 && ownerCloaked != 0)
    {
        // owner is cloaked, treat as invisible - allow this window
    }
    else
    {
        // owner truly visible → filter owned window
    }
}
```

### 5.4 `TelegramDesktop` 加入标题黑名单

**文件**: `GestureSign.CorePlugins/ActivateApp/ActivateAppPlugin.cs`

```csharp
private static readonly HashSet<string> WindowTitleBlacklist = new(StringComparer.OrdinalIgnoreCase)
{
    "HIDENET",
    "QTrayIconMessageWindow",
    "TelegramDesktop"  // ghost 窗口，关闭后残留
};
```

### 5.5 `QTrayIconMessageWindow` 加入标题黑名单

Qt 托盘图标消息窗口，不面向用户。在 hidden scan 中被过滤。
