# GestureSign 开发工具脚本

本目录包含用于简化 GestureSign 开发和测试的自动化脚本。

## 📋 脚本列表

### 构建与运行

| 脚本 | 用途 | 管理员 | 说明 |
|------|------|--------|------|
| `Build-And-Run.ps1` | 一键构建并运行 | 取决于配置 | 支持所有配置，智能选择运行路径 |
| `Build-And-Run.bat` | 批处理包装器 | 取决于配置 | 方便在 VS 中调用 |
| `Build-And-Run-UIAccess.bat` | UIAccess 专用启动 | **必需** | 自动检查管理员权限 |

### 代码签名

| 脚本 | 用途 | 管理员 | 说明 |
|------|------|--------|------|
| `Sign-Code.ps1` | 创建证书并签名 | **必需** | 自动化完整签名流程 |
| `Setup-CodeSigning.ps1` | 签名脚本（中文版） | **必需** | 与 Sign-Code.ps1 功能相同 |

### UIAccess 测试

| 脚本 | 用途 | 管理员 | 说明 |
|------|------|--------|------|
| `Test-UIAccess.ps1` | UIAccess 测试工具 | 部分功能需要 | 检查/启用测试模式，复制到受保护目录 |

### 文档

| 文件 | 用途 |
|------|------|
| `README.md` | UIAccess 完整指南 |
| `VS-External-Tools-Setup.md` | Visual Studio 外部工具配置指南 |

---

## 🚀 快速开始

### 方式 1: 命令行运行

#### Debug 版本（推荐日常开发）
```powershell
# PowerShell
.\scripts\Build-And-Run.ps1 -Configuration Debug

# 或使用批处理
.\scripts\Build-And-Run.bat Debug
```

#### UIAccess 版本（管理员 PowerShell）
```powershell
.\scripts\Build-And-Run.ps1 -Configuration uiAccessRelease

# 或使用专用批处理（自动检查权限）
.\scripts\Build-And-Run-UIAccess.bat
```

### 方式 2: Visual Studio 外部工具

按照 [VS-External-Tools-Setup.md](VS-External-Tools-Setup.md) 配置后：

1. **日常开发**: 工具 → "GestureSign - Build & Run (Debug)"
2. **UIAccess 测试**: 工具 → "GestureSign - Build & Run (UIAccess) [Admin]"
3. **快速重启**: 工具 → "GestureSign - Stop Processes"

### 方式 3: MSBuild 自动化（已配置）

项目已配置自动化 Target：

- ✅ **PreBuild**: 自动停止运行中的 GestureSign 进程
- ✅ **PostBuild (uiAccessRelease)**: 自动运行签名脚本

直接在 Visual Studio 中：
- 按 `F5` 调试运行
- 或 `Ctrl+Shift+B` 构建

---

## 📚 详细文档

### Build-And-Run.ps1

**功能**: 一键构建、清理进程、启动应用

**参数**:
```powershell
-Configuration <Debug|Release|uiAccessRelease|Portable|Centennial>
    构建配置（默认: Debug）

-SkipBuild
    跳过构建步骤，仅运行应用

-RunControlPanel
    同时启动 Control Panel
```

**示例**:
```powershell
# 构建并运行 Debug 版本
.\Build-And-Run.ps1

# 构建并运行 UIAccess 版本（需要管理员）
.\Build-And-Run.ps1 -Configuration uiAccessRelease

# 仅运行（不重新构建）
.\Build-And-Run.ps1 -SkipBuild

# 同时启动 Control Panel
.\Build-And-Run.ps1 -RunControlPanel

# 组合使用
.\Build-And-Run.ps1 -Configuration uiAccessRelease -RunControlPanel
```

**工作流程**:
```
[1/3] 停止运行中的 GestureSign 进程
       ↓
[2/3] 构建解决方案 (dotnet build)
       ↓
[3/3] 启动应用
       - Debug 配置: 带控制台输出 (--log.redirectToStd)
       - UIAccess 配置: 从 C:\Program Files\ 启动
       - 其他配置: 从 D:\wd\soft\GestureSign\ 启动
```

### Sign-Code.ps1

**功能**: 创建自签名证书并签名 UIAccess 可执行文件

**参数**:
```powershell
-BuildConfiguration <string>
    要签名的构建配置（默认: uiAccessRelease）

-SkipSigning
    仅创建证书，不进行签名
```

**示例**:
```powershell
# 创建证书并签名 uiAccessRelease 版本
.\Sign-Code.ps1

# 签名 Release 版本
.\Sign-Code.ps1 -BuildConfiguration Release

# 仅创建证书
.\Sign-Code.ps1 -SkipSigning
```

**执行步骤**:
```
[1/5] 检查现有证书
       ↓
[2/5] 创建自签名证书（如果需要）
       - 主题: CN=GestureSign Development
       - 有效期: 5 年
       - 算法: RSA 2048 + SHA256
       ↓
[3/5] 安装到受信任的根证书存储区
       ↓
[4/5] 查找 signtool.exe
       - Windows SDK
       - ClickOnce SignTool
       ↓
[5/5] 签名可执行文件
       - GestureSign.exe
       - GestureSign.ControlPanel.exe
```

### Test-UIAccess.ps1

**功能**: 测试和配置 UIAccess 运行环境

**参数**:
```powershell
-Mode <CheckTestMode|EnableTestMode|DisableTestMode|CopyToProtectedDir|RunFromProtectedDir>
```

**示例**:
```powershell
# 检查测试模式状态
.\Test-UIAccess.ps1 -Mode CheckTestMode

# 启用测试模式（需要管理员 + 重启）
.\Test-UIAccess.ps1 -Mode EnableTestMode

# 复制到受保护目录（需要管理员）
.\Test-UIAccess.ps1 -Mode CopyToProtectedDir

# 从受保护目录运行
.\Test-UIAccess.ps1 -Mode RunFromProtectedDir
```

---

## ⚙️ MSBuild 自动化配置

### GestureSign.Daemon.csproj

已添加以下 Target：

#### PreBuild: 自动停止进程
```xml
<Target Name="StopGestureSignProcesses" BeforeTargets="BeforeBuild">
  <Exec Command="powershell -NoProfile ... Stop-Process ..." />
</Target>
```

**效果**: 每次构建前自动停止运行中的 GestureSign 进程，避免文件锁定错误。

#### PostBuild (uiAccessRelease): 自动签名
```xml
<Target Name="SignExecutable" AfterTargets="AfterBuild"
        Condition="'$(Configuration)' == 'uiAccessRelease'">
  <Exec Command="powershell ... Sign-Code.ps1 ..." />
</Target>
```

**效果**: 构建 uiAccessRelease 配置后自动运行签名脚本。

### Directory.Build.targets

已配置智能输出路径：

```xml
<PackageOutputDir Condition="'$(Configuration)' == 'uiAccessRelease'">
  C:\Program Files\GestureSign
</PackageOutputDir>
<PackageOutputDir Condition="'$(Configuration)' != 'uiAccessRelease'">
  D:\wd\soft\GestureSign
</PackageOutputDir>
```

**效果**:
- UIAccess 版本自动部署到 `C:\Program Files\GestureSign\`
- 其他版本部署到 `D:\wd\soft\GestureSign\`

---

## 🎯 推荐工作流

### 日常开发（无 UIAccess）

**Visual Studio 中**:
1. 按 `F5` 启动调试
2. 或使用外部工具 "GestureSign - Build & Run (Debug)"

**命令行**:
```powershell
.\scripts\Build-And-Run.ps1
```

**优点**:
- ✅ 快速编译
- ✅ 可附加调试器
- ✅ 控制台日志输出
- ✅ 无需管理员权限

**限制**:
- ❌ 无法向 VSCode、Chrome 等应用发送输入（惯性滚动不生效）

### UIAccess 功能测试

**准备工作（一次性）**:
1. 以管理员身份运行 PowerShell
2. 运行签名脚本创建证书:
   ```powershell
   .\scripts\Sign-Code.ps1 -SkipSigning
   ```

**每次测试**:
1. 以**管理员身份**启动 Visual Studio
2. 使用外部工具 "GestureSign - Build & Run (UIAccess) [Admin]"
3. 或命令行（管理员 PowerShell）:
   ```powershell
   .\scripts\Build-And-Run-UIAccess.bat
   ```

**优点**:
- ✅ 惯性滚动在所有应用中生效
- ✅ 完整的 UIAccess 权限
- ✅ 接近生产环境

**限制**:
- ⚠️ 需要管理员权限
- ⚠️ 无法附加调试器（可查看日志文件）

### 快速切换

**从 Debug 切换到 UIAccess**:
```powershell
# 停止当前运行
Get-Process GestureSign* | Stop-Process -Force

# 启动 UIAccess 版本（管理员 PowerShell）
.\scripts\Build-And-Run-UIAccess.bat
```

**从 UIAccess 切换回 Debug**:
```powershell
# 停止当前运行
Get-Process GestureSign* | Stop-Process -Force

# 启动 Debug 版本（普通 PowerShell）
.\scripts\Build-And-Run.ps1
```

---

## 🔍 故障排除

### 问题 1: "拒绝访问"错误

**症状**: 编译时出现 "无法复制文件" 或 "拒绝访问"

**原因**: GestureSign 进程正在运行，锁定了可执行文件

**解决**:
- ✅ **自动解决**: PreBuild Target 已配置自动停止进程
- 🔧 **手动解决**: 运行 `Get-Process GestureSign* | Stop-Process -Force`

### 问题 2: 签名脚本找不到 signtool.exe

**症状**: 签名脚本报错 "signtool.exe not found"

**原因**: 未安装 Windows SDK

**解决**:
1. 安装 Windows SDK: https://developer.microsoft.com/windows/downloads/windows-sdk/
2. 或安装 Visual Studio 时选择 "Windows SDK" 组件

### 问题 3: UIAccess 版本编译失败 "拒绝访问 C:\Program Files\"

**症状**: 编译时无法写入 C:\Program Files\

**原因**: 未以管理员身份运行

**解决**:
- 以**管理员身份**启动 Visual Studio 或 PowerShell

### 问题 4: 惯性滚动在 VSCode 中不生效

**症状**: UIAccess 版本中惯性滚动仍然无效

**检查清单**:
1. ✅ 是否从 `C:\Program Files\GestureSign\` 运行？
2. ✅ 是否已签名？运行 `Get-AuthenticodeSignature "C:\Program Files\GestureSign\GestureSign.exe"`
3. ✅ 签名是否有效？应显示 `Status : Valid`
4. ✅ 是否启用了测试模式（如果不在受保护目录）？运行 `.\scripts\Test-UIAccess.ps1 -Mode CheckTestMode`

### 问题 5: 外部工具在 VS 中不显示

**症状**: 配置的外部工具在菜单中找不到

**解决**:
1. 检查工具路径是否正确
2. 重启 Visual Studio
3. 确认使用了正确的宏变量（`$(SolutionDir)` 等）

---

## 📖 相关文档

- **UIAccess 完整指南**: [README.md](README.md)
- **Visual Studio 配置**: [VS-External-Tools-Setup.md](VS-External-Tools-Setup.md)
- **项目文档**: [../CLAUDE.md](../CLAUDE.md)

---

**创建日期**: 2026-01-26
**最后更新**: 2026-01-26
**版本**: 1.0
