# Foxmail 主窗口关闭后 ActivateApp 误激活隐藏窗口问题复盘

## 背景

日期：2026-02-28 ~ 2026-03-05
场景：Foxmail 关闭主窗口后，进程仍在（托盘驻留），执行 `ActivateApp -> Foxmail [ProcessName=Foxmail.exe]`。

预期：重新唤起 Foxmail 主窗口。
实际（修复前）：会激活到 IME/系统内部窗口，无法稳定回到 Foxmail 主界面。

---

## 关键现象与日志结论

### 第一轮修复前

- 隐藏窗口扫描命中数量异常高（示例为 `71 hidden windows`）。
- 命中的"Foxmail 同进程窗口"包含输入法窗口（如 `class=IME`、`MSCTFIME UI`）。
- 多窗口分支会优先使用 `_lastActivatedWindows`，导致继续在内部窗口间循环。

### 第一轮修复后 / 第二轮问题

隐藏候选收敛后，出现新问题：`0 windows found`。

Foxmail（Delphi 应用）窗口结构：

| 窗口 | ClassName | Title | 特征 |
| ---- | ---- | ---- | ---- |
| 主窗口 | `TFoxMainFrm.UnicodeClass` | (空) | owner = TApplication 窗口 |
| TApplication | `TApplication` | `Foxmail` | IsWindowVisible=True, 但实际 0 像素大小 |

两个问题导致 Foxmail 主窗口被过滤：

1. **TApplication owner "假可见"**：Delphi 框架的 `TApplication` 窗口是一个 0 像素的消息窗口，`IsWindowVisible` 返回 `True`。原来的 owner 过滤逻辑（有可见 owner → 跳过）会误判主窗口为"对话框/子窗口"。
2. **主窗口无标题**：`TFoxMainFrm.UnicodeClass` 没有标题文本，被 `GetWindowTextLength == 0` 过滤。

### 最终修复后

- 隐藏候选显著收敛，最终 Foxmail 只保留主窗口。
- 典型日志链路：
  - `Activating ...: 1 windows found`
  - `HandleSingleWindow ... isVisible=False, windowState=Minimized`
  - `Showing hidden window: 0x2006A8 'Foxmail'`

结论：当前 Foxmail 主窗口是"隐藏/最小化后可恢复"，不是"已销毁"。
同时，由于 `appWindows.Count != 0`，不会进入 `TryLaunchApplication` 分支。

---

## 根因

`ActivateApp` 的窗口筛选存在三类问题：

1. **隐藏扫描质量不足**：原实现仅检查 `IsWindow(hWnd)`，许多 IME/系统内部窗口进入候选集。
2. **Owner 可见性判断过于简单**：有可见 owner → 一律跳过。未考虑 Delphi TApplication 这类"假可见"框架窗口。
3. **标题要求过于严格**：所有窗口都要求有标题，但 Foxmail 主窗口没有标题。

---

## 修复方案

### 1. 隐藏扫描入口增加 `IsActivatableHiddenWindow` 过滤

过滤规则（与 `IsSwitchableWindow` 可见扫描对齐）：

1. 必须是有效窗口：`IsWindow(hWnd)`
2. 必须是 Win32 不可见窗口：`!IsWindowVisible(hWnd)`
3. `WS_EX_APPWINDOW` → 放行（无论 owner/tool window）
4. `WS_EX_TOOLWINDOW` && !`WS_EX_APPWINDOW` → 过滤
5. Owner 可见性判定（见下方）
6. 排除 `WS_EX_NOACTIVATE`
7. 排除 DWM cloaked 窗口
8. 类名黑名单过滤
9. `requireTitle` 标题检查（可跳过，见下方）

### 2. Owner 可见性判定（IsSwitchableWindow 和 IsActivatableHiddenWindow 统一逻辑）

对于非 `WS_EX_APPWINDOW` 窗口，有 owner 时的判断：

```text
owner 不存在 → 放行
owner 不可见（IsWindowVisible=False）→ 放行
owner 可见但 DWM cloaked → 放行（如 Telegram 旧窗口）
owner 可见但 class 在黑名单（如 TApplication）→ 放行
owner 可见且 class 不在黑名单 → 过滤（对话框/子窗口）
```

### 3. `requireTitle` 参数

当匹配规则包含 `ClassName` 条件时，`requireTitle=false`，跳过标题检查。这使得 Foxmail 的无标题主窗口（通过 ClassName 匹配）能够进入候选集。

统一应用于：缓存验证、可见扫描、隐藏扫描三条路径。

### 4. Hidden scan 后处理 `FilterHiddenCandidates`

三阶段后处理：

1. **黑名单标题替换**：标题在 `WindowTitleBlacklist` 中的窗口（如 HIDENET），尝试用同进程 owned 业务窗口替换
2. **Owner 辅助降权**：被同进程其他候选作为 owner 引用的窗口，排到后面
3. **APPWINDOW 优先**：`WS_EX_APPWINDOW` 窗口排在前面

### 5. 类名黑名单 & 标题黑名单

**WindowClassBlacklist** — 用于 owner 判定和窗口自身过滤：

- `IME` — 输入法窗口
- `MSCTFIME UI` — 微软输入法 UI
- `GDI+ Hook Window Class` — .NET GDI+ 内部窗口
- `TApplication` — Delphi 框架消息窗口（0 像素，假可见）

**WindowTitleBlacklist** — 用于黑名单标题过滤和 hidden scan 后处理：

- `HIDENET` — 通达信辅助根窗口
- `QTrayIconMessageWindow` — Qt 托盘图标消息窗口
- `TelegramDesktop` — Telegram 关闭后残留的 ghost 窗口

---

## 代码改动点

**文件**: `GestureSign.CorePlugins/ActivateApp/ActivateAppPlugin.cs`

- `WindowClassBlacklist` — 新增 `TApplication`、`GDI+ Hook Window Class`
- `WindowTitleBlacklist` — 新增 `HIDENET`、`QTrayIconMessageWindow`、`TelegramDesktop`
- `IsSwitchableWindow(hWnd, requireTitle)` — owner cloaked 检查、owner class 黑名单检查、requireTitle 参数
- `IsActivatableHiddenWindow(hWnd, requireTitle)` — 与 `IsSwitchableWindow` 对齐的隐藏扫描过滤
- `GetMatchingWindows` — 计算 `requireTitle`，传递给缓存验证/可见/隐藏三条路径
- `FilterHiddenCandidates` — hidden scan 后处理（替换 + 降权 + 排序）
- `FindOwnedWindowInSameProcess` — 在同进程中查找被 own 的业务窗口
- `SortByAppWindowPriority` — 提取的 APPWINDOW 优先排序静态方法
- 激活日志增加 owner 信息

**文件**: `ManagedWinapi/Windows/SystemWindow.cs`

- `TryActivateWindow` — showHidden 分支改为 SW_SHOW 优先、SW_RESTORE 兜底（保留最大化状态）

---

## 验证结果

1. 关闭 Foxmail 主窗口（保留托盘进程）后，`ActivateApp` 可恢复主窗口。
2. 随后多次从其他应用切回 Foxmail，均能命中主窗口。
3. `NextApplication` 链路仍可正常切到 Foxmail 业务窗（如 `TMailViewerForm.UnicodeClass`）。

---

## 已解决的待 Review 关注点

1. ~~`owner==0` 的限制是否会误伤某些托盘应用~~ → 已改为完整的 owner 可见性判定链（cloaked + class 黑名单），不再简单要求 owner==0。
2. 是否需要在后续版本增加"首轮状态收敛"策略 — 暂未实施，当前方案已足够稳定。
3. ~~是否引入"要求有标题"作为可选模式~~ → 已实现为 `requireTitle` 参数，由规则条件自动决定。
