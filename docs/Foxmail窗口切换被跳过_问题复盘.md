# Foxmail 窗口切换被跳过问题复盘

## 问题现象

触发链路：

1. 从 VSCode 激活 Foxmail
2. 再激活通达信
3. 执行「切换到下一个窗口」

预期：从通达信切到 Foxmail。  
实际（修复前）：跳到 VSCode，表现为“Foxmail 被跳过”。

## 关键日志特征（修复前）

- `ActivateApp` 能命中 Foxmail 业务窗口 `0x2006A8`（有标题）。
- 前台窗口会落到 Foxmail 无标题框架窗口 `0x1E142C`（`TFoxMainFrm.UnicodeClass`）。
- `NextApplication` 切换时未将 Foxmail 作为下一候选，直接切到其他应用。

## 根因

`NextApplication` 的可切换窗口筛选与当前窗口锚点定位，和系统 Alt+Tab 行为不一致，主要是两点：

1. 代表窗口判定过于简化  
原逻辑接近 `GetLastActivePopup(rootOwner) == hWnd` 的单步判断，无法稳定覆盖 Foxmail 这类“无标题 root + 有标题 popup”结构。

2. 当前前台窗口可能不在候选列表  
当前台是框架/宿主窗口（如 `0x1E142C`）时，`IndexOf` 失败后会回落到 `windows[0]` 推进，导致顺序跳变。

## 解决方案

### 1) 对齐 Alt+Tab 代表窗口算法

在 `IsSwitchableWindow` 中引入 root-owner + last-active-popup 的迭代判定（Raymond Chen 方案），只保留代表窗口。

同时保留已有过滤：

- ToolWindow / AppWindow
- NOACTIVATE
- DWM Cloaked
- Shell 内部窗口类名黑名单

对于无标题候选窗口，增加受限放行条件：仅当其 root-owner 组内存在可见有标题同进程窗口时，才纳入候选。  
补充说明：`HasVisibleTitledPopupUnderRootOwner` 内 `GetWindowTextLength(rootOwner) != 0` 的 early return 用于处理“候选窗口无标题，但其 rootOwner 与候选不同且 rootOwner 自身有标题”的情况，不是放宽无标题窗口本身。

### 2) 当前窗口索引回退链

新增 `ResolveCurrentWindowIndex(...)`，按以下顺序回退：

1. `windows.IndexOf(currentWindow)`
2. `rootOwner`
3. `lastActivePopup(rootOwner)`
4. 同 `rootOwner` 的兄弟代表窗口
5. 同进程窗口（PID）

仅在全部失败时返回 `-1`，避免不必要回落到 `windows[0]`。

## 代码改动点

- [NextApplication.cs:167](../GestureSign.CorePlugins/NextApplication.cs#L167)
  - `Gestured`：使用 `ResolveCurrentWindowIndex` 替代直接 `IndexOf`
- [NextApplication.cs:215](../GestureSign.CorePlugins/NextApplication.cs#L215)
  - `IsSwitchableWindow`：Alt+Tab 代表窗口判定 + 无标题受限放行
- [NextApplication.cs:260](../GestureSign.CorePlugins/NextApplication.cs#L260)
  - 新增 `ResolveCurrentWindowIndex(...)`
- [NextApplication.cs:315](../GestureSign.CorePlugins/NextApplication.cs#L315)
  - 新增 `IsAltTabRepresentativeWindow(...)`
- [NextApplication.cs:336](../GestureSign.CorePlugins/NextApplication.cs#L336)
  - 新增 `HasVisibleTitledPopupUnderRootOwner(...)`

## 验证结果

复测同一链路后，`NextApplication` 从通达信正确切到 Foxmail（`0x2006A8`），不再跳过。

## 兼容性与风险评估

- 影响范围：仅 `NextApplication` 插件的窗口枚举与当前窗口锚点逻辑。
- 兼容性：算法更接近系统 Alt+Tab，理论上比原实现更稳。
- 风险点：PID 兜底在“同进程多个独立顶层窗口”时可能命中非用户预期窗口，但该分支仅在前 4 层都失败时触发，属于可接受 trade-off（优于直接回落 `windows[0]`）。

## 额外说明

排障期间新增的高频诊断日志已移除，仅保留原有切换日志，避免常驻日志噪音。
