# Foxmail 主窗口关闭后 ActivateApp 误激活隐藏窗口问题复盘

## 背景

日期：2026-02-28  
场景：Foxmail 关闭主窗口后，进程仍在（托盘驻留），执行 `ActivateApp -> Foxmail [ProcessName=Foxmail.exe]`。

预期：重新唤起 Foxmail 主窗口。  
实际（修复前）：会激活到 IME/系统内部窗口，无法稳定回到 Foxmail 主界面。

---

## 关键现象与日志结论

### 修复前

- 隐藏窗口扫描命中数量异常高（示例为 `71 hidden windows`）。
- 命中的“Foxmail 同进程窗口”包含输入法窗口（如 `class=IME`、`MSCTFIME UI`）。
- 多窗口分支会优先使用 `_lastActivatedWindows`，导致继续在内部窗口间循环。

### 修复后（当前状态）

- 隐藏候选显著收敛，最终 Foxmail 只保留 `0x2006A8`。
- 典型日志链路：
  - `Activating ...: 1 windows found`
  - `HandleSingleWindow ... isVisible=False, windowState=Minimized`
  - `Showing hidden window: 0x2006A8 'Foxmail'`

结论：当前 Foxmail 主窗口是“隐藏/最小化后可恢复”，不是“已销毁”。  
同时，由于 `appWindows.Count != 0`，不会进入 `TryLaunchApplication` 分支（这是符合现有设计的）。

---

## 根因

`ActivateApp` 在“可见窗口未命中 -> 进入隐藏窗口扫描”时，原实现仅检查 `IsWindow(hWnd)`，窗口质量过滤不足，导致：

1. 许多与目标应用无关或不可激活的隐藏窗口进入候选集。
2. 规则为 `ProcessName=Foxmail.exe` 时，会把同进程内部窗口一起纳入。
3. `_lastActivatedWindows` 容易被历史脏句柄污染，触发首轮误激活。

---

## 修复方案（最小改动）

仅在 `includeHidden=true` 的扫描入口增加过滤方法 `IsActivatableHiddenWindow`，不引入 `NextApplication` 的完整 Alt+Tab 代表窗口算法。

过滤规则：

1. 必须是有效窗口：`IsWindow(hWnd)`
2. 必须是隐藏窗口：`!IsWindowVisible(hWnd)`
3. 必须是顶层窗口：`GetWindow(hWnd, GW_OWNER) == 0`
4. 排除 ToolWindow（除非显式带 `WS_EX_APPWINDOW`）
5. 排除 `WS_EX_NOACTIVATE`
6. 类名黑名单（当前最小集）：
   - `IME`
   - `MSCTFIME UI`

说明：

- 黑名单使用 `HashSet<string>(StringComparer.OrdinalIgnoreCase)`，便于后续按观测增量扩展。
- 诊断阶段曾临时增加 `Hidden candidate` 日志，现已移除，避免常驻噪音。

---

## 代码改动点

- [ActivateAppPlugin.cs:35](../GestureSign.CorePlugins/ActivateApp/ActivateAppPlugin.cs#L35)
  - 新增 `HiddenWindowClassBlacklist`（忽略大小写）
- [ActivateAppPlugin.cs:62](../GestureSign.CorePlugins/ActivateApp/ActivateAppPlugin.cs#L62)
  - 新增 `GetClassName` P/Invoke
- [ActivateAppPlugin.cs](../GestureSign.CorePlugins/ActivateApp/ActivateAppPlugin.cs)
  - 新增 `WS_EX_NOACTIVATE`
- [ActivateAppPlugin.cs:314](../GestureSign.CorePlugins/ActivateApp/ActivateAppPlugin.cs#L314)
  - 隐藏扫描入口由 `IsWindow` 改为 `IsActivatableHiddenWindow`
- [ActivateAppPlugin.cs:351](../GestureSign.CorePlugins/ActivateApp/ActivateAppPlugin.cs#L351)
  - 新增 `IsActivatableHiddenWindow` 实现

---

## 验证结果

在 2026-02-28 的连续实测中：

1. 关闭 Foxmail 主窗口（保留托盘进程）后，`ActivateApp` 可恢复 `0x2006A8 'Foxmail'`。
2. 随后多次从其他应用切回 Foxmail，均能命中主窗口。
3. `NextApplication` 链路仍可正常切到 Foxmail 业务窗（如 `TMailViewerForm.UnicodeClass`）。

---

## 待 Review 关注点

1. `owner==0` 的限制是否会误伤某些托盘应用（其可唤醒入口是 owned window）。
2. 是否需要在后续版本增加“首轮状态收敛”策略（例如更短缓存或更严格首轮校验），以减少历史 `_lastActivatedWindows` 对首轮行为的影响。
3. 是否保持当前“黑名单 + 结构过滤”的策略，或在后续引入更强白名单条件（例如要求有标题）作为可选模式。
