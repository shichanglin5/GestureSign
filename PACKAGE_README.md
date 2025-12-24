# GestureSign 自动打包配置说明

## 概述

本项目已配置自动打包功能，使用 MSBuild 目标（Targets）实现。当你在 Visual Studio 中构建 **Release** 配置时，会自动将程序和所有依赖文件复制到：

**`D:\wd\soft\GestureSign`**

## 使用方法

### 方法一：Visual Studio 构建（推荐）

1. 在 Visual Studio 中打开解决方案
2. 选择 **Release** 配置（顶部工具栏）
3. 点击 **生成** → **生成解决方案**（或按 `Ctrl+Shift+B`）
4. 构建完成后，文件会自动复制到 `D:\wd\soft\GestureSign`

### 方法二：命令行构建

```bash
# 构建 Release 版本（会自动打包）
dotnet build GestureSign.sln -c Release

# 或使用 MSBuild
msbuild GestureSign.sln /p:Configuration=Release
```

### 方法三：仅打包（不重新构建）

如果已经构建过，只想重新打包：

```bash
# 运行打包目标
msbuild GestureSign.sln /t:Package /p:Configuration=Release
```

## 配置文件说明

### Directory.Build.targets

这个文件定义了自动打包的 MSBuild 目标，包含：

- **Package 目标**：在 Release 构建后自动执行
- **CleanPackage 目标**：清理时删除打包目录
- **PackageOutputDir 属性**：定义输出目录（默认：`D:\wd\soft\GestureSign`）

### 自定义输出目录

如果需要修改输出目录，有两种方法：

#### 方法 1：修改 Directory.Build.targets

编辑 `Directory.Build.targets` 文件，修改第 6 行：

```xml
<PackageOutputDir>你的自定义路径</PackageOutputDir>
```

#### 方法 2：命令行参数

```bash
dotnet build -c Release /p:PackageOutputDir="D:\custom\path"
```

## 打包内容

自动打包会复制以下文件到输出目录：

### 主程序
- ✓ `GestureSign.exe` - 后台服务（Daemon）
- ✓ `GestureSignControlPanel.exe` - 配置界面

### 程序集
- ✓ `*.dll` - 所有依赖的 DLL 文件
- ✓ `*.pdb` - 调试符号文件

### 配置文件
- ✓ `*.runtimeconfig.json` - .NET 运行时配置
- ✓ `*.deps.json` - 依赖清单

### 资源文件
- ✓ `Languages\` - 语言文件目录
- ✓ `Defaults\` - 默认配置（如果存在）
- ✓ `Plugins\` - 插件目录（如果存在）
- ✓ `StartGestureSign.bat` - 启动脚本（如果存在）

## 禁用自动打包

如果不想在每次 Release 构建时都打包，可以：

### 临时禁用

构建时添加参数：

```bash
dotnet build -c Release /p:SkipPackage=true
```

### 永久禁用

在 `Directory.Build.targets` 中，修改第 12 行的条件：

```xml
<!-- 在条件中添加 AND '$(SkipPackage)' != 'true' -->
<Target Name="Package" AfterTargets="Build"
        Condition="'$(Configuration)' == 'Release' AND '$(SkipPackage)' != 'true' AND ...">
```

## 在 Visual Studio 中查看打包日志

1. 生成后，查看 **输出** 窗口（`Ctrl+Alt+O`）
2. 选择 **显示输出来源：生成**
3. 查找 "Packaging" 相关的消息

## 故障排除

### 问题：打包目录没有创建

**解决**：确保父目录 `D:\wd\soft\` 存在。如果不存在，手动创建或修改 `Directory.Build.targets` 中的路径。

### 问题：某些文件没有复制

**解决**：检查构建输出目录 `bin\Release\net8.0-windows10.0.19041.0\` 中是否存在这些文件。如果不存在，说明构建配置有问题。

### 问题：Debug 配置也在打包

**解决**：检查 `Directory.Build.targets` 第 12 行的条件，确保包含 `'$(Configuration)' == 'Release'`。

## 高级用法

### 添加其他文件到打包

在 `Directory.Build.targets` 的 `<Target Name="Package">` 中添加：

```xml
<Copy SourceFiles="你的文件路径"
      DestinationFolder="$(PackageOutputDir)"
      SkipUnchangedFiles="true" />
```

### 打包前执行自定义任务

在 `<Target Name="Package">` 中添加 `BeforeTargets` 属性：

```xml
<Target Name="MyCustomTask" BeforeTargets="Package">
  <Message Text="执行自定义任务..." Importance="high" />
  <!-- 你的自定义任务 -->
</Target>
```

## 清理打包目录

清理解决方案时，打包目录也会被删除：

```bash
dotnet clean GestureSign.sln
```

或在 Visual Studio 中：**生成** → **清理解决方案**
