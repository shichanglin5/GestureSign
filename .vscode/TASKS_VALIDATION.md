# GestureSign Tasks.json 验证报告

## 概述
已完成 tasks.json 重构，移除所有对 scripts/ 目录的依赖，使用原子任务和复合工作流实现完整功能。

## 原子任务列表

### 进程管理
- ✅ `stop-gesturesign` - 停止所有 GestureSign 进程

### 基础构建任务
- ✅ `debug: build` - Debug 构建
- ✅ `debug: clean` - Debug 清理
- ✅ `debug: rebuild` - Debug 重建（复合：clean → build）
- ✅ `release: build` - Release 构建
- ✅ `release: clean` - Release 清理
- ✅ `release: rebuild` - Release 重建（复合：clean → build）
- ✅ `uiAccessRelease: build` - UIAccess 构建
- ✅ `uiAccessRelease: clean` - UIAccess 清理
- ✅ `uiAccessRelease: rebuild` - UIAccess 重建（复合：clean → build）

### 代码签名任务（需要管理员权限）
- ✅ `signing: create-cert` - 创建自签名证书
- ✅ `signing: install-cert` - 安装证书到受信任根存储
- ✅ `signing: sign-daemon` - 签名 GestureSign.exe
- ✅ `signing: sign-controlpanel` - 签名 GestureSignControlPanel.exe
- ✅ `signing: verify` - 验证签名状态

### 文件操作任务（需要管理员权限）
- ✅ `copy: to-programfiles` - 复制文件到 C:\Program Files\GestureSign

### Windows 系统任务
- ✅ `system: check-testmode` - 检查测试签名模式状态
- ✅ `system: enable-testmode` - 启用测试签名模式（需要管理员+重启）
- ✅ `system: disable-testmode` - 禁用测试签名模式（需要管理员+重启）

### 运行任务
- ✅ `run: daemon-debug` - 运行 Debug 版本 Daemon（带日志）
- ✅ `run: controlpanel-debug` - 运行 Debug 版本 Control Panel
- ✅ `run: daemon-release` - 运行 Release 版本 Daemon
- ✅ `run: controlpanel-release` - 运行 Release 版本 Control Panel
- ✅ `run: daemon-uiaccess` - 运行 UIAccess 版本 Daemon（从 Program Files）
- ✅ `run: controlpanel-uiaccess` - 运行 UIAccess 版本 Control Panel（从 Program Files）

## 复合工作流任务

### workflow: debug-full ⭐ (默认构建任务 Ctrl+Shift+B)
**流程**:
1. stop-gesturesign - 停止现有进程
2. debug: build - 构建 Debug 版本
3. run: daemon-debug - 启动 Daemon（带日志）
4. run: controlpanel-debug - 启动 Control Panel

**用途**: 日常开发最常用工作流

### workflow: release-full
**流程**:
1. stop-gesturesign - 停止现有进程
2. release: build - 构建 Release 版本
3. run: daemon-release - 启动 Daemon

**用途**: 测试 Release 版本

### workflow: uiaccess-setup
**流程**:
1. signing: create-cert - 创建证书
2. signing: install-cert - 安装证书

**用途**: 首次 UIAccess 开发环境设置（需要管理员权限）

### workflow: uiaccess-full
**流程**:
1. stop-gesturesign - 停止现有进程
2. uiAccessRelease: rebuild - 重建 UIAccess 版本
3. signing: sign-daemon - 签名 Daemon
4. signing: sign-controlpanel - 签名 Control Panel
5. copy: to-programfiles - 复制到受保护目录
6. signing: verify - 验证签名

**用途**: 完整 UIAccess 构建和部署（需要管理员权限）

## 验证测试结果

### 已验证
- ✅ `debug: build` - 构建成功，文件正确复制到 D:\wd\soft\GestureSign
- ✅ .exe 文件生成 - GestureSign.exe 和 GestureSignControlPanel.exe 都正确生成
- ✅ 构建时间优化 - 增量构建约 4.5 秒
- ✅ PostBuildEvent 时机 - Package 目标在 .exe 创建后执行
- ✅ `stop-gesturesign` - 进程停止任务正常工作
- ✅ PowerShell 命令语法 - 修复了所有单引号/双引号冲突问题
- ✅ 参数保留 - `--log.redirectToStd` 等命令行参数的单引号正确保留

### 待用户测试
以下任务需要在 VS Code 中实际运行验证:

#### 基础工作流测试
1. 按 `Ctrl+Shift+B` 执行默认构建任务 `workflow: debug-full`
   - 验证进程停止
   - 验证构建成功
   - 验证 Daemon 和 Control Panel 启动

2. 运行 `workflow: release-full`
   - 验证 Release 版本构建
   - 验证 Daemon 启动（无控制台窗口）

#### UIAccess 工作流测试（需要以管理员身份启动 VS Code）
1. 首次设置：运行 `workflow: uiaccess-setup`
   - 验证证书创建
   - 验证证书安装到受信任根存储

2. 完整部署：运行 `workflow: uiaccess-full`
   - 验证 UIAccess 构建
   - 验证代码签名
   - 验证文件复制到 C:\Program Files\GestureSign
   - 验证签名有效性

3. 测试模式管理：
   - 运行 `system: check-testmode` 检查状态
   - 运行 `system: enable-testmode` 启用（需要重启）

## 实现细节

### 技术亮点
1. **零脚本依赖** - 所有 PowerShell 逻辑内联到 tasks.json
2. **原子任务设计** - 每个任务完成单一职责
3. **复合工作流** - 使用 dependsOn 和 dependsOrder: "sequence" 组合任务
4. **智能路径检测** - 自动处理 net8.0-windows10.0.19041.0 子目录
5. **跳过已签名文件** - signtool verify 检查避免重复签名
6. **详细反馈** - 彩色输出提供清晰的状态信息

### PowerShell 内联技巧
```json
{
  "command": "powershell",
  "args": [
    "-NoProfile",
    "-Command",
    "复杂的 PowerShell 单行命令，使用 ; 分隔语句"
  ]
}
```

### signtool.exe 路径检测
自动搜索以下位置:
1. `C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe`
2. `C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe`
3. `C:\Program Files\Microsoft SDKs\Windows\*\bin\*\x64\signtool.exe`

## 与 scripts/ 文件的对比

| 脚本文件 | 替代任务 | 状态 |
|---------|---------|------|
| Build-And-Run.ps1 | workflow: debug-full / release-full | ✅ 可删除 |
| Quick-Build-UIAccess.ps1 | workflow: uiaccess-full | ✅ 可删除 |
| Sign-Code.ps1 | signing: create-cert + sign-daemon/controlpanel | ✅ 可删除 |
| Test-UIAccess.ps1 | system: check/enable/disable-testmode | ✅ 可删除 |
| Setup-CodeSigning.ps1 | workflow: uiaccess-setup | ✅ 可删除 |
| Open-AdminTerminal.ps1 | (手动以管理员身份启动 VS Code) | ✅ 可删除 |

## 下一步行动

### 1. 用户测试
用户需要在 VS Code 中测试以下关键工作流:
- [ ] `workflow: debug-full` (Ctrl+Shift+B)
- [ ] `workflow: release-full`
- [ ] `workflow: uiaccess-setup` (管理员模式)
- [ ] `workflow: uiaccess-full` (管理员模式)

### 2. 删除 scripts/ 目录
确认所有测试通过后，可以删除以下文件:
```bash
rm scripts/Build-And-Run.ps1
rm scripts/Quick-Build-UIAccess.ps1
rm scripts/Sign-Code.ps1
rm scripts/Test-UIAccess.ps1
rm scripts/Setup-CodeSigning.ps1
rm scripts/Open-AdminTerminal.ps1
```

### 3. 文档更新
更新以下文档提及的脚本引用:
- README.md
- CLAUDE.md
- 其他开发文档

## 常见问题

### Q: 为什么有些任务需要管理员权限？
A: 以下操作需要管理员权限:
- 安装证书到 LocalMachine\Root
- 复制文件到 C:\Program Files\
- 修改 Windows 启动配置 (bcdedit)

### Q: 如何以管理员身份运行 VS Code？
A: 右键 VS Code 图标 → "以管理员身份运行"

### Q: UIAccess 版本为什么必须在 C:\Program Files\ 下运行？
A: Windows 安全策略要求 UIAccess 应用必须:
1. 位于受保护目录 (Program Files)
2. 使用受信任证书签名
3. 清单文件设置 uiAccess="true"

### Q: 测试签名模式是什么？
A: 允许 Windows 接受自签名证书，开发 UIAccess 应用时必须启用。启用后桌面右下角会显示"测试模式"水印。

## 修改历史

### 2026-01-27 - 完整重构
- 移除所有对 scripts/*.ps1 的依赖
- 实现 25+ 个原子任务
- 创建 4 个复合工作流
- 内联所有 PowerShell 逻辑
- 添加详细注释和文档
