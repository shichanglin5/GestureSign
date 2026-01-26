# VS Code 配置说明

本目录包含 GestureSign 项目在 VS Code 中的开发配置。

## 📁 文件说明

### launch.json - 调试配置
包含以下调试配置（按 F5 或点击"运行和调试"）：

| 配置名称 | 类型 | 用途 | 说明 |
|---------|------|------|------|
| **Debug Daemon** | 单进程 | 调试后台服务 | 自动停止进程、构建并启动 GestureSign.exe，带控制台日志输出 |
| **Debug ControlPanel** | 单进程 | 调试控制面板 | 自动构建并启动 GestureSignControlPanel.exe |
| **Debug Daemon + ControlPanel** | 复合配置 | 同时调试两个进程 | **并行启动** Daemon 和 ControlPanel，两个调试会话独立运行 |
| **Attach to GestureSign Process** | 附加 | 附加到运行中的 Daemon | 用于调试已运行的进程 |
| **Attach to ControlPanel Process** | 附加 | 附加到运行中的 ControlPanel | 用于调试已运行的进程 |

**调试特性**：
- ✅ 自动停止现有进程（避免端口冲突）
- ✅ 自动构建项目（增量构建）
- ✅ 控制台日志输出（`--log.redirectToStd`）
- ✅ 支持断点、变量查看、调用堆栈
- ✅ 禁用 JIT 优化，便于调试
- ✅ 复合配置支持同时调试多个进程

### settings.json - 工作区设置
VS Code 项目特定配置：
- C# 扩展配置
- 文件排除（bin、obj、.vs）
- 格式化设置
- 终端默认 PowerShell
- 编辑器配置（120 列标尺）

### extensions.json - 推荐扩展
推荐安装的 VS Code 扩展列表，打开项目时会提示安装。

## 📚 相关文档

- [VS Code C# 调试文档](https://code.visualstudio.com/docs/languages/csharp)
- [Tasks 配置参考](https://code.visualstudio.com/docs/editor/tasks)
- [Launch 配置参考](https://code.visualstudio.com/docs/editor/debugging)
- [项目构建脚本](../scripts/BUILD-AND-RUN.md)
- [UIAccess 完整指南](../scripts/README.md)

---

**创建日期**: 2026-01-26
**版本**: 1.0
