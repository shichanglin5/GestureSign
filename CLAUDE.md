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

### Review 驱动修复工作流

当存在 `.review/` 目录下的审查文档（如 `code-review-*.md`）时，按以下流程推进：

1. **按优先级顺序修复** — 严格按文档中 P0 → P1 → P2 顺序逐条处理，不要跳跃
2. **修复 → 测试 → 标记 → 下一条** — 每修完一个问题，立即补充/运行单元测试确认通过，然后在 review 文档中将该条标记为 `✅ Fixed`（标题前缀），再进入下一条。不要批量修完再补测试
3. **遇到阻塞立即停下沟通** — 如果修复方案不确定、涉及架构决策、或发现问题描述与实际代码不符，停下来和用户确认，不要猜测推进
4. **中途不中断** — 除阻塞外，连续推进直到当前优先级批次全部完成

### git 规范

- commit msg 使用中文

### 回复语言

- 使用中文回答

### 构建规范

- 构建使用 vscode task `workflow: uiaccess-full`, 该任务会执行完整的构建部署流程 (停止进程、构建、签名、复制、启动)

### 测试规范

#### 修复必须附带回归测试
- 每个 Bug Fix 必须同时提交对应的回归测试，覆盖触发 bug 的具体条件
- 禁止仅修复代码而不补充测试——缺失测试是回归问题反复出现的根本原因

#### 测试必须覆盖边界条件与降级路径
- 不能只写 happy path 测试；必须覆盖修复所触及的边界条件
- 对包含 fallback/降级逻辑的代码，必须验证每条降级路径均可达且行为正确
- 对含有多个分支的分类逻辑（如 GestureClassifier），每个分支至少一个测试用例

#### 三类高优先级测试场景
1. **时序/异步测试**: 涉及异步操作（如训练模式启停、IPC 通信）的修复，必须测试竞态条件和过期响应处理
2. **跨链路一致性测试**: 当同一数据/逻辑在多个路径使用时（如运行时 vs 训练模式 vs UI），必须验证各路径行为一致
3. **降级路径测试**: 主路径失败后的 fallback 逻辑必须有独立测试，确保不会被意外短路为死代码
