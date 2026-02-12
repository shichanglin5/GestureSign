# GestureSign 项目文档 - Claude Code 快速入门

> **最后更新**: 2026-01-24
> **项目**: GestureSign - Windows 手势识别应用程序

## 📋 项目概述

GestureSign 是一个基于 .NET 8.0 的 Windows 手势识别应用,支持触摸板和触摸屏的多点触控手势识别,用于执行自定义操作和快捷方式。

### 核心功能
- 多点触控手势识别与自定义动作绑定
- 可视化手势配置界面
- 插件化架构支持扩展
- 多语言支持
- 多种部署方式(标准/便携/Microsoft Store)

---

## 🛠️ 技术栈

### .NET 平台
- **目标框架**: .NET 8.0 (net8.0-windows / net8.0-windows10.0.19041.0)
- **C# 版本**: Latest (启用最新语言特性)
- **编译特性**:
  - Nullable reference types: enabled
  - AllowUnsafeBlocks: true (部分项目)
  - BinaryFormatter 序列化警告抑制 (迁移中)

### UI 框架
- **WPF** - 主配置界面 (ControlPanel)
- **Windows Forms** - 后台守护进程 (Daemon)
- **MahApps.Metro 2.4.10** - 现代 UI 样式库

### 关键依赖
```
Newtonsoft.Json 13.0.3      - JSON 序列化
MahApps.Metro 2.4.10        - WPF UI 框架
Sentry 4.3.0                - 错误报告
System.Management 8.0.0     - Windows 管理
System.Drawing.Common 8.0.0 - 图形支持
```

### 开发环境要求
- **最低 Windows 版本**: Windows 10 20H2 (Build 19041)
- **开发工具**: Visual Studio 2022 或更高版本
- **.NET SDK**: .NET 8.0 SDK
- **特殊权限**: Daemon 需要 UIAccess 权限 (uiAccessRelease 配置)

---

## 📂 项目结构

```
GestureSign/
├── GestureSign.ControlPanel/      # WPF 配置界面 (Exe)
│   ├── UI/                        # UI 视图和控件
│   ├── ViewModel/                 # MVVM 视图模型
│   └── Properties/                # 应用清单和资源
│
├── GestureSign.Daemon/            # 后台服务 (Exe)
│   ├── Input/                     # 输入捕获 (PointCapture, MessageWindow)
│   ├── Triggers/                  # 触发器 (HotKey, ContinuousGesture)
│   ├── Filtration/                # 输入过滤
│   └── Surface/                   # 视觉反馈渲染
│
├── GestureSign.Common/            # 共享核心逻辑 (Library)
│   ├── Applications/              # 应用程序管理与匹配
│   ├── Gestures/                  # 手势定义与管理
│   ├── Plugins/                   # 插件架构核心
│   ├── InterProcessCommunication/ # IPC (Named Pipes)
│   ├── Input/                     # 输入事件定义
│   ├── Localization/              # 多语言支持
│   ├── Log/                       # 日志系统
│   ├── Configuration/             # 配置管理 (AppConfig)
│   └── UI/                        # 共享 UI 组件
│
├── GestureSign.CorePlugins/       # 内置插件库 (Library)
│   └── [各种内置动作插件]
│
├── GestureSign.PointPatterns/     # 手势识别引擎 (Library)
│   └── [手势匹配算法]
│
├── ManagedWinapi/                 # Windows API 封装 (Library)
│   └── [Win32 API 互操作]
│
├── WindowsInput/                  # 输入模拟 (Signed Library)
│   └── [键盘/鼠标模拟]
│
└── [ExtraPlugins]/                # 额外插件
    ├── ClipboardMatch/
    └── TextCopyer/
```

---

## 🏗️ 架构设计

### 分层架构

```
┌─────────────────────────────────────────────────┐
│  表示层 (Presentation Layer)                    │
│  - GestureSign.ControlPanel (WPF UI)           │
│  - GestureSign.CorePlugins (Action UI)         │
└─────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────┐
│  业务逻辑层 (Business Logic Layer)              │
│  - GestureSign.Common (Managers, Models)       │
│  - GestureSign.PointPatterns (Recognition)     │
└─────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────┐
│  基础设施层 (Infrastructure Layer)              │
│  - GestureSign.Daemon (Input Capture)          │
│  - ManagedWinapi (Windows API)                 │
│  - WindowsInput (Input Simulation)             │
└─────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────┐
│  横切关注点 (Cross-cutting Concerns)            │
│  - Logging, Localization, IPC, Configuration   │
└─────────────────────────────────────────────────┘
```

### 关键设计模式

#### 1. Singleton 模式
- **GestureManager** - `GestureSign.Common/Gestures/GestureManager.cs`
- **ApplicationManager** - `GestureSign.Common/Applications/ApplicationManager.cs`
- **PluginManager** - `GestureSign.Common/Plugins/PluginManager.cs`
- **PointCapture** - `GestureSign.Daemon/Input/PointCapture.cs`

#### 2. 观察者/事件模式
```csharp
// 典型事件发布
ApplicationManager.ApplicationSaved event
GestureManager.GestureSaved event
AppConfig.ConfigChanged event
PointCapture.GestureRecognized event
```

#### 3. 插件架构
- **接口**: `IPlugin`, `IHostControl`
- **管理器**: `PluginManager` - 动态加载插件程序集
- **扩展点**: 插件可自定义 UI 和执行逻辑

#### 4. IPC 通信
- **技术**: Named Pipes
- **场景**: ControlPanel ↔ Daemon 进程间通信
- **用途**: 配置更新、状态同步

#### 5. Manager/Factory 模式
- 集中式管理器 (ApplicationManager, GestureManager, PluginManager)
- 延迟初始化 (Lazy<T>)
- 订阅输入事件并协调动作执行

---

## 🔧 构建配置

### 构建配置 (共 5 种)

| 配置名称 | OutputType | 用途 |
|---------|-----------|------|
| **Debug** | Exe | 调试版本,带控制台输出 |
| **Release** | WinExe | 发布版本,无控制台窗口 |
| **Portable** | WinExe | 便携版本,目录式运行 |
| **Centennial** | WinExe | Microsoft Store/AppX 打包 |
| **uiAccessRelease** | WinExe | UIAccess 提升权限版本 |

### 输出路径
```
..\bin\[配置名]\net8.0-windows[10.0.19041.0]\
```

### 条件编译符号
```csharp
#if ConvertedDesktopApp  // Microsoft Store 版本差异
#if Portable             // 便携版本安装差异
#if uiAccess             // UIAccess 提升权限差异
```

### 编译器警告抑制
```xml
<NoWarn>SYSLIB0011;CA1416</NoWarn>
<!-- SYSLIB0011: BinaryFormatter 过时警告 (迁移中) -->
<!-- CA1416: 平台兼容性警告 -->
```

---

## 🚀 开发指南

### 命令行参数

#### Daemon 命令行参数
```bash
GestureSign.Daemon.exe --log.redirectToStd  # 将日志重定向到标准输出 (VS 调试用)
```

### 调试启动配置

#### Daemon 调试 (launchSettings.json)
```json
{
  "profiles": {
    "GestureSign.Daemon": {
      "commandName": "Project",
      "commandLineArgs": "--log.redirectToStd"
    }
  }
}
```

### 关键入口点

#### Daemon 入口
- **文件**: [GestureSign.Daemon/Program.cs](GestureSign.Daemon/Program.cs)
- **主要职责**: 初始化输入捕获、触发器、IPC 服务器

#### ControlPanel 入口
- **文件**: [GestureSign.ControlPanel/App.xaml.cs](GestureSign.ControlPanel/App.xaml.cs)
- **主要职责**: 初始化 WPF 应用、连接 Daemon

### 手势识别流程

```
用户触摸 → MessageWindow (Hook)
         → PointCapture.ProcessPoint()
         → PointPatternAnalyzer.GetPointPatternType()
         → GestureRecognized Event
         → ApplicationManager.OnGestureRecognized()
         → PluginManager.ExecuteAction()
```

### 输入捕获架构

#### 核心组件
- **MessageWindow** ([GestureSign.Daemon/Input/MessageWindow.cs](GestureSign.Daemon/Input/MessageWindow.cs:1-500))
  - Windows 消息窗口
  - 处理 WM_POINTER* 消息

- **PointCapture** ([GestureSign.Daemon/Input/PointCapture.cs](GestureSign.Daemon/Input/PointCapture.cs:1-800))
  - 单例模式
  - 管理触摸点捕获和手势识别
  - 触发 GestureRecognized 事件

#### 输入流程
```
Windows Touch API → MessageWindow → PointCapture → PointPatternAnalyzer → Event
```

### 插件开发

#### 创建新插件
1. 实现 `IPlugin` 接口
2. 可选实现 UI 配置界面
3. 放入 Plugins 目录
4. PluginManager 自动加载

#### 插件接口
```csharp
public interface IPlugin
{
    string Name { get; }
    string Description { get; }
    void Execute(IHostControl host, ActionExecutionContext context);
}
```

---

## 📁 关键文件速查

### 配置文件
- **解决方案**: [GestureSign.sln](GestureSign.sln)
- **全局构建配置**: [Directory.Build.props](Directory.Build.props), [Directory.Build.targets](Directory.Build.targets)
- **应用程序配置**: `GestureSign.Common/Configuration/AppConfig.cs`

### 管理器 (Managers)
- **手势管理**: [GestureSign.Common/Gestures/GestureManager.cs](GestureSign.Common/Gestures/GestureManager.cs)
- **应用程序管理**: [GestureSign.Common/Applications/ApplicationManager.cs](GestureSign.Common/Applications/ApplicationManager.cs)
- **插件管理**: [GestureSign.Common/Plugins/PluginManager.cs](GestureSign.Common/Plugins/PluginManager.cs)

### 输入处理
- **点捕获**: [GestureSign.Daemon/Input/PointCapture.cs](GestureSign.Daemon/Input/PointCapture.cs)
- **消息窗口**: [GestureSign.Daemon/Input/MessageWindow.cs](GestureSign.Daemon/Input/MessageWindow.cs)
- **输入提供者**: [GestureSign.Daemon/Input/InputProvider.cs](GestureSign.Daemon/Input/InputProvider.cs)

### 手势识别
- **模式分析**: `GestureSign.PointPatterns/PointPatternAnalyzer.cs`

### 通信
- **IPC**: `GestureSign.Common/InterProcessCommunication/`

---

## 🎯 常见任务快速指南

### 添加新的手势动作插件
1. 在 [GestureSign.CorePlugins/](GestureSign.CorePlugins/) 创建新类
2. 实现 `IPlugin` 接口
3. 添加 UI 配置 (WinForms UserControl)
4. 更新 Resources.resx 添加本地化字符串

### 修改手势识别逻辑
- 主要文件: [GestureSign.PointPatterns/](GestureSign.PointPatterns/)
- 关注 `PointPatternAnalyzer` 类

### 调试输入捕获问题
1. 启动 Daemon 项目,使用 `--log.redirectToStd` 参数
2. 检查 [PointCapture.cs](GestureSign.Daemon/Input/PointCapture.cs) 的日志输出
3. 验证 [MessageWindow.cs](GestureSign.Daemon/Input/MessageWindow.cs) 是否接收到触摸消息

### 更新 UI 样式
- WPF 样式: [GestureSign.ControlPanel/](GestureSign.ControlPanel/) 中的 XAML 文件
- MahApps.Metro 主题配置: App.xaml

### 多语言支持
- 资源文件位置: `Properties/Resources.*.resx`
- 使用 `LocalizationProvider` 获取本地化字符串

---

## 💡 Claude Code 使用建议

### 快速定位问题
1. **手势识别问题** → 查看 [GestureSign.PointPatterns/](GestureSign.PointPatterns/)
2. **输入捕获问题** → 查看 [GestureSign.Daemon/Input/](GestureSign.Daemon/Input/)
3. **UI 问题** → 查看 [GestureSign.ControlPanel/](GestureSign.ControlPanel/)
4. **插件问题** → 查看 [GestureSign.CorePlugins/](GestureSign.CorePlugins/) 或 [GestureSign.Common/Plugins/](GestureSign.Common/Plugins/)
5. **配置问题** → 查看 [GestureSign.Common/Configuration/](GestureSign.Common/Configuration/)

### 代码搜索提示
- 查找所有单例: 搜索 `Lazy<` 或 `static readonly`
- 查找所有事件: 搜索 `event EventHandler`
- 查找插件实现: 搜索 `IPlugin`
- 查找 IPC 通信: 搜索 `NamedPipe`

### git 规范

- commit msg 使用中文

### 回复语言

- 使用中文回答

### 构建规范

- 构建使用 `uiAccessRelease` 配置: `dotnet build GestureSign.sln -c uiAccessRelease`

### uiAccessRelease 完整构建部署流程

完整流程包含多个步骤（停止进程、构建、签名、复制、启动），涉及管理员提权和文件覆盖。**执行前必须请求用户确认同意。**

脚本位于 [scripts/](scripts/) 目录，按以下顺序执行：

```bash
# 1. 停止运行中的 GestureSign 进程（可能需要管理员提权）
powershell -NoProfile -File scripts/Stop-GestureSign.ps1

# 2. 删除旧日志
powershell -NoProfile -File scripts/Manage-Log.ps1 -Action delete

# 3. 构建 uiAccessRelease
dotnet build GestureSign.sln -c uiAccessRelease -v minimal

# 4. 代码签名（自动创建/复用自签名证书，需要管理员提权）
powershell -NoProfile -File scripts/Sign-Code.ps1

# 5. 复制构建输出到 C:\Program Files\GestureSign\
powershell -NoProfile -File scripts/Copy-ToProgramFiles.ps1

# 6. 启动 UIAccess Daemon
powershell -NoProfile -File scripts/Run-GestureSign.ps1 -Config uiaccess
```

对应 VSCode tasks.json 中的 `workflow: uiaccess` 任务。
