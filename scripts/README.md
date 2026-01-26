# UIAccess 代码签名与测试指南

本目录包含用于配置 GestureSign UIAccess 功能的脚本和工具。

## 📋 什么是 UIAccess?

UIAccess (User Interface Privilege Isolation Access) 允许应用程序向具有更高权限级别的窗口发送输入事件,绕过 Windows UIPI 安全限制。

### UIAccess 的用途

在 GestureSign 中,UIAccess 用于解决 **惯性滚动插件** 无法向某些应用发送输入的问题:

- ✅ **无 UIAccess**: 可以向记事本、资源管理器等普通应用发送输入
- ❌ **无 UIAccess**: 无法向 VSCode、Chrome、管理员权限应用发送输入
- ✅ **有 UIAccess**: 可以向所有应用发送输入,包括提升权限的应用

### UIAccess 的要求

启用 UIAccess 的应用必须满足以下 **三个条件**:

1. **数字签名**: 可执行文件必须有有效的数字签名(可以是自签名)
2. **受信任证书**: 签名证书必须安装在受信任的根证书存储区
3. **受保护位置**: 必须从受保护目录运行 (C:\Program Files\, C:\Windows\System32\ 等)

## 🚀 快速开始

### 步骤 1: 创建自签名证书并签名

在 PowerShell **管理员**模式下运行:

```powershell
cd scripts
.\Setup-CodeSigning.ps1
```

此脚本会:
- ✅ 创建自签名代码签名证书(有效期 5 年)
- ✅ 将证书安装到受信任的根证书存储区
- ✅ 对 uiAccessRelease 版本的所有可执行文件进行签名

### 步骤 2: 选择测试方式

#### 方式 A: 启用 Windows 测试模式(推荐开发调试)

**优点**:
- ✅ 可以从任何位置运行 UIAccess 应用
- ✅ 方便开发调试,无需每次复制到受保护目录
- ✅ 可以直接查看日志文件

**缺点**:
- ⚠️ 桌面右下角会显示"测试模式"水印
- ⚠️ 需要重启计算机

**启用步骤** (需要管理员权限):

```powershell
.\Test-UIAccess.ps1 -Mode EnableTestMode
# 按提示重启计算机
```

重启后,可以直接运行:

```powershell
& "..\bin\uiAccessRelease\net8.0-windows10.0.19041.0\GestureSign.exe"
```

#### 方式 B: 复制到受保护目录

**优点**:
- ✅ 无需测试模式,无水印
- ✅ 符合生产环境部署方式

**缺点**:
- ❌ 每次修改代码后需要重新复制
- ❌ 无法直接从 Visual Studio F5 调试
- ❌ 查看日志需要管理员权限

**使用步骤** (需要管理员权限):

```powershell
.\Test-UIAccess.ps1 -Mode CopyToProtectedDir
.\Test-UIAccess.ps1 -Mode RunFromProtectedDir
```

## 🛠️ 脚本说明

### Setup-CodeSigning.ps1

**功能**: 创建自签名证书并对可执行文件签名

**参数**:
- `-BuildConfiguration <string>`: 要签名的构建配置(默认: uiAccessRelease)
- `-SkipSigning`: 仅创建证书,不进行签名

**示例**:

```powershell
# 创建证书并签名 uiAccessRelease 版本
.\Setup-CodeSigning.ps1

# 创建证书并签名 Release 版本
.\Setup-CodeSigning.ps1 -BuildConfiguration Release

# 仅创建证书
.\Setup-CodeSigning.ps1 -SkipSigning
```

### Test-UIAccess.ps1

**功能**: 测试和运行 UIAccess 版本

**参数**:
- `-Mode <CheckTestMode|EnableTestMode|DisableTestMode|CopyToProtectedDir|RunFromProtectedDir>`

**示例**:

```powershell
# 检查当前测试模式状态
.\Test-UIAccess.ps1 -Mode CheckTestMode

# 启用测试模式(需要管理员 + 重启)
.\Test-UIAccess.ps1 -Mode EnableTestMode

# 禁用测试模式(需要管理员 + 重启)
.\Test-UIAccess.ps1 -Mode DisableTestMode

# 复制到受保护目录(需要管理员)
.\Test-UIAccess.ps1 -Mode CopyToProtectedDir

# 从受保护目录运行
.\Test-UIAccess.ps1 -Mode RunFromProtectedDir
```

## 🔍 调试策略

### 开发阶段 (推荐)

**使用 Debug 配置** - 无 UIAccess,但可以 F5 调试:

1. Visual Studio 中设置启动项目为 `GestureSign.Daemon`
2. 设置启动参数: `--log.redirectToStd`
3. F5 启动调试,日志输出到控制台
4. 修改代码后可以立即 F5 重新调试

**限制**: 无法向 VSCode、Chrome 等应用发送输入

### 测试 UIAccess 功能

**方式 1: 启用测试模式** (推荐):

```powershell
# 一次性启用测试模式
.\Test-UIAccess.ps1 -Mode EnableTestMode
# 重启计算机

# 编译 UIAccess 版本
dotnet build ..\GestureSign.sln -c uiAccessRelease

# 直接运行(可以从任何位置)
& "..\bin\uiAccessRelease\net8.0-windows10.0.19041.0\GestureSign.exe"
```

**方式 2: 受保护目录** (接近生产环境):

```powershell
# 编译 UIAccess 版本
dotnet build ..\GestureSign.sln -c uiAccessRelease

# 复制并运行
.\Test-UIAccess.ps1 -Mode CopyToProtectedDir
.\Test-UIAccess.ps1 -Mode RunFromProtectedDir
```

### 查看日志

**Debug 配置**:
- 日志输出到控制台 (使用 `--log.redirectToStd` 参数)
- 或查看日志文件: `%LOCALAPPDATA%\GestureSign\GestureSign.log`

**UIAccess 配置**:
- 查看日志文件: `%LOCALAPPDATA%\GestureSign\GestureSign.log`
- 使用 VS Code 或记事本实时监控日志文件

## ⚠️ 常见问题

### Q1: 为什么 UIAccess 版本无法从 Visual Studio F5 启动?

**A**: UIAccess 要求应用必须从受保护目录 (C:\Program Files\) 运行,而 VS 的 Debug 输出目录 (bin\Debug\) 不是受保护位置。

**解决方案**:
- 日常开发使用 Debug 配置(无 UIAccess)
- 需要测试 UIAccess 功能时,使用测试模式或复制到受保护目录

### Q2: 启用测试模式安全吗?

**A**: 测试模式会降低系统安全性,允许运行未签名或自签名的驱动程序和应用。

**建议**:
- ✅ 开发机可以启用
- ❌ 生产环境不建议启用
- ✅ 完成开发后可以禁用: `.\Test-UIAccess.ps1 -Mode DisableTestMode`

### Q3: 自签名证书是否可以用于发布版本?

**A**: 不建议。自签名证书仅在本机受信任。

**发布建议**:
- 购买商业代码签名证书 (如 DigiCert, Sectigo)
- 或申请免费的开源项目证书
- 用户下载后才能正常运行 UIAccess 版本

### Q4: 如何验证签名是否成功?

```powershell
# 检查签名状态
Get-AuthenticodeSignature "..\bin\uiAccessRelease\net8.0-windows10.0.19041.0\GestureSign.exe"

# 或使用脚本
.\Test-UIAccess.ps1 -Mode CheckTestMode
```

输出应显示 `Status : Valid`

## 📊 配置对比

| 配置 | UIAccess | 调试 | 签名要求 | 运行位置 | 适用场景 |
|------|----------|------|----------|----------|----------|
| Debug | ❌ | ✅ F5 | 无 | 任意 | 日常开发 |
| Release | ❌ | ❌ | 无 | 任意 | 普通发布 |
| uiAccessRelease | ✅ | ❌ | 必须 | 受保护/测试模式 | UIAccess 功能测试 |
| uiAccessRelease + 测试模式 | ✅ | ⚠️ 附加 | 必须 | 任意 | UIAccess 开发调试 |

## 🔗 相关文档

- [Windows UIPI 文档](https://docs.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)
- [代码签名最佳实践](https://docs.microsoft.com/en-us/windows-hardware/drivers/install/authenticode)
- [UIAccess 详解](https://docs.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview#user-interface-privilege-isolation)

## 📝 维护

**证书过期时间**: 自签名证书有效期 5 年

**查看证书**:
```powershell
Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like '*GestureSign*' }
```

**删除证书**:
```powershell
Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=GestureSign Development' } | Remove-Item
Get-ChildItem Cert:\CurrentUser\Root | Where-Object { $_.Subject -eq 'CN=GestureSign Development' } | Remove-Item
```

---

**创建日期**: 2026-01-26
**最后更新**: 2026-01-26
