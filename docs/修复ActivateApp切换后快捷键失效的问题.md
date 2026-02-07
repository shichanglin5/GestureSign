# 修复 ActivateApp 切换到通达信后 Win+Tab 失效的问题

## 问题现象

通过 ActivateApp 手势从其他应用切换到通达信后，立即执行 Win+Tab 手势无效：
- 从 VS Code 等应用 → ActivateApp 切换到通达信 → Win+Tab 不生效（连续两次均失败）
- 鼠标左键点击通达信窗口后再执行 Win+Tab → 正常打开任务视图
- 仅通达信（`TdxW_MainFrame_Class`）有此问题，切换到其他应用后 Win+Tab 正常

## 根因分析

### ForceSetForegroundWindow 的 Alt 键技巧污染键盘状态

`ForceSetForegroundWindow`（`SystemWindow.cs`）使用三层策略激活窗口：

```
方法1: SetForegroundWindow(hWnd) → 对通达信失败（后台进程无法直接设置前台）
  ↓
方法2: AttachThreadInput → keybd_event(VK_MENU↓↑) → SetForegroundWindow → SetFocus → DetachThreadInput
  ↓
方法3: SetWindowPos(HWND_TOP) → SetForegroundWindow（兜底）
```

方法2中使用 `keybd_event(VK_MENU)` 模拟 Alt 键按下/释放，目的是让 Windows 认为调用进程收到了用户输入，从而获得 `SetForegroundWindow` 的调用权限。

**问题**：`keybd_event` 只将事件放入共享输入队列，不等待处理。随后 `DetachThreadInput` 分离线程时，Alt 释放事件可能未同步到目标线程的独立队列，导致 Alt 键在系统层面残留为"按下"状态。后续 `SendInput` 发送 Win+Tab 时，系统实际看到 **Alt+Win+Tab**，无法触发任务视图。

### 为什么鼠标点击能修复

鼠标点击通过正常输入管道重置修饰键状态，清除残留的 Alt 键。

### 为什么仅通达信受影响

对其他应用方法1（`SetForegroundWindow`）通常直接成功，Alt 键技巧不触发。通达信的 `SetForegroundWindow` 总是失败（可能因为窗口类 `TdxW_MainFrame_Class` 的特殊属性或权限差异），每次都走到方法2的 Alt 键技巧。

## 修复方案

### 修改 1（核心）：替换 Alt 键技巧为 mouse_event（SystemWindow.cs）

`SetForegroundWindow` 的限制条件是"调用线程最近处理过输入事件"。`mouse_event(MOUSEEVENTF_MOVE, 0, 0, 0, 0)` 同样满足此条件，且：
- 不修改任何键盘修饰键状态
- 零位移不移动光标
- 不触发窗口内的点击等操作

```csharp
// 修改前：
keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

// 修改后：
mouse_event(MOUSEEVENTF_MOVE, 0, 0, 0, UIntPtr.Zero);
```

新增 P/Invoke 声明：

```csharp
[DllImport("user32.dll")]
private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

private const uint MOUSEEVENTF_MOVE = 0x0001;
```

### 修改 2（防御性增强）：HotKeyPlugin 发送前清理残留修饰键（HotKeyPlugin.cs）

作为深度防御，在 `SendKeysSeparately` 和 `SendShortcutKeys` 方法开头检查并清理不属于当前快捷键的残留修饰键。利用 `KeyboardKey.IsGloballyPressed`（内部调用 `GetAsyncKeyState`）检测状态，`KeyboardKey.Release()` 清理。

```csharp
private static void ClearResidualModifierKeys(HotKeySettings settings)
{
    // 只清理不属于当前快捷键的修饰键
    CheckAndReleaseKey(Keys.LMenu, settings.Alt);
    CheckAndReleaseKey(Keys.RMenu, settings.Alt);
    CheckAndReleaseKey(Keys.LControlKey, settings.Control);
    CheckAndReleaseKey(Keys.RControlKey, settings.Control);
    CheckAndReleaseKey(Keys.LShiftKey, settings.Shift);
    CheckAndReleaseKey(Keys.RShiftKey, settings.Shift);
    CheckAndReleaseKey(Keys.LWin, settings.Windows);
    CheckAndReleaseKey(Keys.RWin, settings.Windows);
}

private static void CheckAndReleaseKey(Keys key, bool isIntendedModifier)
{
    if (isIntendedModifier) return;
    var kbKey = new KeyboardKey(key);
    if (kbKey.IsGloballyPressed)
    {
        Logging.LogDebug($"[HotKeyPlugin] Clearing residual modifier key: {key}");
        kbKey.Release();
    }
}
```

### 修改 3（优化）：移除 SendShortcutKeys 中不必要的 Sleep(30)（HotKeyPlugin.cs）

`SendInput` 是原子操作，所有键事件一次性注入系统输入队列，调用返回后事件已全部入队。`.Sleep(30)` 在 `SendInput` 之后无实际意义。

```csharp
// 修改前：
simulator.Keyboard.KeyPress(keys.ToArray()).Sleep(30);
simulator.Keyboard.ModifiedKeyStroke(modifiedKeys, keys).Sleep(30);
simulator.Keyboard.KeyPress(modifiedKeys.ToArray()).Sleep(30);

// 修改后：
simulator.Keyboard.KeyPress(keys.ToArray());
simulator.Keyboard.ModifiedKeyStroke(modifiedKeys, keys);
simulator.Keyboard.KeyPress(modifiedKeys.ToArray());
```

注意：`SendKeysSeparately`（安全模式）中的 `Thread.Sleep(50)` 不动——逐键 `keybd_event` 发送场景下延迟是有意义的，给目标应用处理按键事件的时间窗口。

## 性能评估

### 修改 1：mouse_event 替换 keybd_event

| 项目 | 开销 |
|------|------|
| `mouse_event` P/Invoke 调用 | ~1-2μs（与 `keybd_event` 相当） |
| 减少一次调用 | 原来 2 次 `keybd_event`（按下+释放），现在 1 次 `mouse_event` |
| 总变化 | 略有减少，可忽略 |

### 修改 2：ClearResidualModifierKeys

| 项目 | 开销 |
|------|------|
| `GetAsyncKeyState` × 8 次调用 | ~8μs（每次 ~1μs，纯内核态查询，无上下文切换） |
| `KeyboardKey` 对象创建 × 8 | ~1-2μs（轻量对象，无资源分配） |
| 正常路径总开销 | **~10μs**（无残留键时，仅查询不释放） |
| 异常路径额外开销 | `keybd_event` 释放 ~1-2μs/次（极少触发） |

对比手势识别总耗时（通常 10-100ms 量级），10μs 的额外开销不到 0.1%，完全可忽略。

### 修改 3：移除 Sleep(30)

| 项目 | 变化 |
|------|------|
| 移除 `Thread.Sleep(30)` | 减少 30ms 固定延迟 |
| 效果 | 快捷键响应速度提升约 30ms |

`SendInput` 是原子操作，所有 INPUT 结构一次性通过单个系统调用注入输入队列。函数返回后事件已全部入队，不存在需要等待处理完成的场景。移除 Sleep 不影响可靠性，反而改善响应延迟。

注意：`SendKeysSeparately`（安全模式）中的 `Thread.Sleep(50)` 保留不动。`keybd_event` 逐键发送场景下，每次调用是独立的系统调用，目标应用需要时间窗口逐个处理按键事件，延迟是必要的。

## 涉及文件

| 文件 | 修改内容 |
|------|---------|
| `ManagedWinapi/Windows/SystemWindow.cs` | 替换 Alt 键技巧为 mouse_event + 新增 P/Invoke 声明 |
| `GestureSign.CorePlugins/HotKey/HotKeyPlugin.cs` | 新增 ClearResidualModifierKeys 防御性清理 + 移除 Sleep(30) |

## 参考资料

- [SetForegroundWindow - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- [mouse_event - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mouse_event)
- [GetAsyncKeyState - Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate)
