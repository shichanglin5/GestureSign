# VS Code 配置说明

本目录包含 GestureSign 项目在 VS Code 中的开发配置。

## 📁 文件说明

### launch.json - 调试配置
包含以下调试配置（按 F5 或点击"运行和调试"）：

| 配置名称 | 用途 | 说明 |
|---------|------|------|
| **Debug Daemon (Debug)** | 调试后台服务 | 启动 GestureSign.exe，带控制台日志输出 |
| **Debug ControlPanel (Debug)** | 调试控制面板 | 启动 GestureSignControlPanel.exe |
| **Debug Both** | 同时调试两个进程 | 先启动 Daemon，后启动 ControlPanel |
| **Attach to GestureSign Process** | 附加到运行中的 Daemon | 用于调试已运行的进程 |
| **Attach to ControlPanel Process** | 附加到运行中的 ControlPanel | 用于调试已运行的进程 |

**调试特性**：
- ✅ 自动停止现有进程（避免端口冲突）
- ✅ 自动构建项目
- ✅ 控制台日志输出（`--log.redirectToStd`）
- ✅ 支持断点、变量查看、调用堆栈
- ✅ 禁用 JIT 优化，便于调试

### tasks.json - 任务配置
包含常用开发任务（Ctrl+Shift+P → "Tasks: Run Task"）：

#### 🔨 构建任务
- `build-debug` - 构建 Debug 配置
- `build-release` - 构建 Release 配置
- `build-uiAccessRelease` - 构建 UIAccess 配置
- `build-debug-daemon` - 仅构建 Daemon 项目
- `build-debug-controlpanel` - 仅构建 ControlPanel 项目

#### 🔄 重新构建任务
- `rebuild-debug` - 强制重新构建 Debug（--no-incremental）
- `rebuild-uiAccessRelease` - 强制重新构建 UIAccess

#### 🧹 清理任务
- `clean-debug` - 清理 Debug 输出
- `clean-uiAccessRelease` - 清理 UIAccess 输出
- `clean-all-configs` - 删除所有 bin 和 obj 目录（彻底清理）

#### 🚀 运行任务（不调试）
- `run-daemon-debug` - 运行 Daemon（Debug 配置）
- `run-controlpanel-debug` - 运行 ControlPanel（Debug 配置）

#### ⚙️ UIAccess 专用任务
- `build-and-run-uiAccessRelease` - 完整 UIAccess 构建并运行
- `quick-build-uiAccessRelease` - 快速构建 UIAccess（需要管理员）
- `sign-code-uiAccessRelease` - 代码签名

#### 🔧 工具任务
- `stop-gesturesign-processes` - 停止所有 GestureSign 进程

#### 📦 复合任务
- `stop-clean-rebuild-debug` - 停止进程 → 清理 → 重新构建（**默认构建任务**，Ctrl+Shift+B）
- `stop-clean-rebuild-uiAccessRelease` - UIAccess 完整清理重构建

### settings.json - 工作区设置
VS Code 项目特定配置：
- C# 扩展配置
- 文件排除（bin、obj、.vs）
- 格式化设置
- 终端默认 PowerShell
- 编辑器配置（120 列标尺）

### extensions.json - 推荐扩展
推荐安装的 VS Code 扩展列表，打开项目时会提示安装。

## 🚀 快速开始

### 1. 日常开发调试

**方式 A: 使用调试面板**
1. 打开 VS Code 调试面板（Ctrl+Shift+D）
2. 选择 "Debug Daemon (Debug)"
3. 按 F5 启动调试

**方式 B: 使用命令面板**
1. 按 Ctrl+Shift+P
2. 输入 "Debug: Select and Start Debugging"
3. 选择配置

### 2. 运行构建任务

**使用快捷键**：
- `Ctrl+Shift+B` - 运行默认构建任务（stop-clean-rebuild-debug）

**使用命令面板**：
1. 按 `Ctrl+Shift+P`
2. 输入 "Tasks: Run Task"
3. 选择要运行的任务

**使用任务终端**：
1. 按 `Ctrl+` ` （反引号）打开终端
2. 点击终端右上角的 "+" 下拉菜单
3. 选择 "Configure Tasks" 查看所有任务

### 3. UIAccess 开发

#### 方法 A: 使用 VS Code 任务
```
Ctrl+Shift+P → Tasks: Run Task → quick-build-uiAccessRelease
```
⚠️ 需要以管理员身份启动 VS Code

#### 方法 B: 使用外部脚本
```powershell
# 在管理员 PowerShell 中
.\scripts\Quick-Build-UIAccess.ps1
```

### 4. 调试技巧

#### 附加到运行中的进程
如果 GestureSign 已经在运行：
1. 打开调试面板（Ctrl+Shift+D）
2. 选择 "Attach to GestureSign Process"
3. 按 F5

#### 同时调试 Daemon 和 ControlPanel
1. 使用 "Debug Both (Daemon + ControlPanel)" 配置
2. 会先启动 Daemon，然后自动启动 ControlPanel
3. 两个进程的日志都会显示在集成终端

#### 调试 UIAccess 版本
⚠️ **UIAccess 版本无法直接调试**（由于安全限制），但可以：
- 查看日志文件
- 使用 `--log.redirectToStd` 参数运行（非 UIAccess）
- 在 Debug 配置下测试功能，再切换到 UIAccess

## 📝 常见任务清单

### 修复"项目被跳过"问题
```
Ctrl+Shift+P → Tasks: Run Task → clean-all-configs
然后
Ctrl+Shift+B （默认构建任务）
```

### 切换配置构建
```
# 构建 Debug
Ctrl+Shift+P → Tasks: Run Task → build-debug

# 构建 UIAccess
Ctrl+Shift+P → Tasks: Run Task → build-uiAccessRelease
```

### 停止所有 GestureSign 进程
```
Ctrl+Shift+P → Tasks: Run Task → stop-gesturesign-processes
```

### 完整 UIAccess 构建流程（管理员）
```
Ctrl+Shift+P → Tasks: Run Task → build-and-run-uiAccessRelease
```

## ⌨️ 快捷键速查

| 快捷键 | 功能 |
|--------|------|
| `F5` | 启动调试（当前选择的配置） |
| `Ctrl+F5` | 运行（不调试） |
| `Shift+F5` | 停止调试 |
| `Ctrl+Shift+F5` | 重新启动调试 |
| `F9` | 切换断点 |
| `F10` | 单步跳过 |
| `F11` | 单步进入 |
| `Shift+F11` | 单步跳出 |
| `Ctrl+Shift+B` | 运行构建任务 |
| `Ctrl+Shift+P` | 命令面板 |
| `Ctrl+` ` | 切换终端 |

## 🔍 故障排除

### 问题 1: "Cannot find preLaunchTask"
**原因**: tasks.json 配置有误或不存在

**解决**:
1. 重新打开 VS Code
2. 确保 tasks.json 文件存在且格式正确
3. 按 Ctrl+Shift+P → "Developer: Reload Window"

### 问题 2: 调试时提示 "Could not find the task 'stop-gesturesign-processes'"
**原因**: 任务名称不匹配

**解决**: 检查 launch.json 中的 `preLaunchTask` 是否与 tasks.json 中的 `label` 匹配

### 问题 3: UIAccess 任务失败 "Access Denied"
**原因**: VS Code 没有以管理员身份运行

**解决**:
1. 关闭 VS Code
2. 右键 VS Code 图标 → "以管理员身份运行"
3. 重新打开项目

### 问题 4: C# 扩展无法工作
**原因**: 未安装推荐扩展或 OmniSharp 未启动

**解决**:
1. 查看 VS Code 右下角状态栏
2. 如果显示 "Omnisharp: xxx"，说明正在加载
3. 如果没有，安装 "C# Dev Kit" 扩展
4. 重新加载窗口（Ctrl+Shift+P → "Developer: Reload Window"）

### 问题 5: 构建输出太多警告看不清错误
**解决**: 在 tasks.json 中将 `-v minimal` 改为 `-v quiet`

---

## 📚 相关文档

- [VS Code C# 调试文档](https://code.visualstudio.com/docs/languages/csharp)
- [Tasks 配置参考](https://code.visualstudio.com/docs/editor/tasks)
- [Launch 配置参考](https://code.visualstudio.com/docs/editor/debugging)
- [项目构建脚本](../scripts/BUILD-AND-RUN.md)
- [UIAccess 完整指南](../scripts/README.md)

---

**创建日期**: 2026-01-26
**版本**: 1.0
