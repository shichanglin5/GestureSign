# Visual Studio 外部工具配置指南

## 配置步骤

1. 打开 Visual Studio
2. 菜单: **工具 (Tools)** → **外部工具 (External Tools)**
3. 点击 **添加 (Add)** 创建新工具

## 配置 1: Debug 版本快速启动

**标题 (Title)**: `GestureSign - Build & Run (Debug)`

**命令 (Command)**:
```
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe
```

**参数 (Arguments)**:
```
-NoProfile -ExecutionPolicy Bypass -File "$(SolutionDir)scripts\Build-And-Run.ps1" -Configuration Debug
```

**初始目录 (Initial directory)**:
```
$(SolutionDir)scripts
```

**选项**:
- ✅ 使用输出窗口 (Use Output window)
- ✅ 提示输入参数 (Prompt for arguments) - 可选

## 配置 2: UIAccess 版本启动（管理员）

**标题 (Title)**: `GestureSign - Build & Run (UIAccess) [Admin]`

**命令 (Command)**:
```
$(SolutionDir)scripts\Build-And-Run-UIAccess.bat
```

**参数 (Arguments)**:
```
（留空）
```

**初始目录 (Initial directory)**:
```
$(SolutionDir)scripts
```

**选项**:
- ⚠️ **重要**: 需要以管理员身份运行 Visual Studio

## 配置 3: 仅停止进程

**标题 (Title)**: `GestureSign - Stop Processes`

**命令 (Command)**:
```
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe
```

**参数 (Arguments)**:
```
-NoProfile -ExecutionPolicy Bypass -Command "Get-Process -Name 'GestureSign*' -ErrorAction SilentlyContinue | Stop-Process -Force"
```

**初始目录 (Initial directory)**:
```
$(SolutionDir)
```

**选项**:
- ✅ 使用输出窗口 (Use Output window)

## 配置 4: 启动 Control Panel

**标题 (Title)**: `GestureSign - Run Control Panel`

**命令 (Command)**:
```
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe
```

**参数 (Arguments)**:
```
-NoProfile -ExecutionPolicy Bypass -File "$(SolutionDir)scripts\Build-And-Run.ps1" -Configuration Debug -SkipBuild -RunControlPanel
```

**初始目录 (Initial directory)**:
```
$(SolutionDir)scripts
```

**选项**:
- ✅ 使用输出窗口 (Use Output window)

## 使用方法

配置完成后，可以通过以下方式快速访问：

1. **菜单访问**: 工具 (Tools) → [您配置的工具名称]
2. **快捷键**: 可以在 **工具 (Tools)** → **选项 (Options)** → **环境 (Environment)** → **键盘 (Keyboard)** 中为外部工具设置快捷键
   - 搜索 `Tools.ExternalCommand1` (数字对应工具顺序)
   - 设置自定义快捷键，例如 `Ctrl+Shift+F5`

## Visual Studio 宏变量

常用的宏变量：
- `$(SolutionDir)` - 解决方案目录
- `$(SolutionFileName)` - 解决方案文件名
- `$(ProjectDir)` - 当前项目目录
- `$(Configuration)` - 当前构建配置 (Debug/Release)

## 示例工作流

### 日常开发 (Debug 模式)
1. 按 `F5` 或点击 "调试" → 启动 VS 附加调试器
2. 或使用外部工具 "GestureSign - Build & Run (Debug)" → 快速启动不附加调试器

### UIAccess 功能测试
1. 以**管理员身份**启动 Visual Studio
2. 工具 → "GestureSign - Build & Run (UIAccess) [Admin]"
3. 等待构建和签名完成
4. 应用自动启动并获得 UIAccess 权限

### 快速重启
1. 工具 → "GestureSign - Stop Processes"
2. 工具 → "GestureSign - Build & Run (Debug)"

## 提示

⚠️ **管理员权限**: UIAccess 版本需要管理员权限编译（因为要写入 C:\Program Files\）

✅ **自动化**: 现在项目已配置 PreBuild 自动停止进程，无需手动清理

✅ **签名自动化**: uiAccessRelease 配置会在 PostBuild 自动运行签名脚本
