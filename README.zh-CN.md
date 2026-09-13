# MyGestures

[English](README.md) | [简体中文](README.zh-CN.md)

[![构建和发布](https://github.com/qpingcode/MyGestures/actions/workflows/release.yml/badge.svg)](https://github.com/qpingcode/MyGestures/actions/workflows/release.yml)

独立运行的 Windows 鼠标手势应用，从 MyTools 抽离。无需启动 MyTools，也无需 Node.js 运行时。

## 功能

- 按住鼠标右键绘制手势，松开后执行已绑定的动作。
- 关闭设置窗口后继续在托盘运行。双击托盘图标重新打开设置，从托盘菜单退出。`--background` 可仅启动托盘，不显示设置。
- 可编辑手势名称、方向、目标进程、键盘快捷键或鼠标动作，以及各项启用状态。
- 目标进程为空时全局生效；同一手势的进程专属配置优先。
- 录制手势或动作时暂时暂停检测。
- 设置修改后自动保存。

## 系统要求

- Windows 10 或 Windows 11，x64。
- WebView2 Runtime（通常已预装）。
- 完整版为 self-contained，无需单独安装 .NET Desktop Runtime。
- 精简版需要已安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。
- 从源码构建需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 以及 Node.js/npm。

## 安装

MyGestures 提供完整版（内置 .NET 运行时）与精简版（需要 .NET 8 Desktop Runtime）。两种版本各自保持独立的更新路径。

<!-- mygestures-downloads:start -->
| 类型 | 通道 | 版本 | 完整安装包 | 便携版 |
| --- | --- | --- | --- | --- |
| **完整版** | **Stable** | — | 尚未发布 | 尚未发布 |
| **精简版** | **Stable** | — | 尚未发布 | 尚未发布 |
| **完整版** | **Beta** | 0.0.5 | [下载](https://github.com/qpingcode/MyGestures/releases/download/v0.0.5/MyGestures-0.0.5-windows-x64-full-setup.exe) | [下载](https://github.com/qpingcode/MyGestures/releases/download/v0.0.5/MyGestures-0.0.5-windows-x64-full-portable.zip) |
| **精简版** | **Beta** | 0.0.5 | [下载](https://github.com/qpingcode/MyGestures/releases/download/v0.0.5/MyGestures-0.0.5-windows-x64-lite-setup.exe) | [下载](https://github.com/qpingcode/MyGestures/releases/download/v0.0.5/MyGestures-0.0.5-windows-x64-lite-portable.zip) |
<!-- mygestures-downloads:end -->






Stable 是推荐通道。推送名为 `release-YYYY-MM-DD` 的 git tag（例如 `release-2026-09-13`）会发布稳定版。每次推送到 `main` 都会发布 Beta。

便携包解压后直接运行 `MyGestures.exe` 即可。

> Authenticode 签名尚未启用。Windows SmartScreen 可能显示“未知发布者”警告，请仅从本仓库的 Releases 页面下载 MyGestures。

## 从源码构建

```powershell
cd MyGestures/Web
npm ci
npm run build
cd ../..
dotnet build MyGestures/MyGestures.csproj -p:OutputPath=bin/AgentVerification/
dotnet test MyGestures.Test/MyGestures.Test.csproj -p:OutputPath=bin/AgentVerification/
./MyGestures/bin/AgentVerification/MyGestures.exe
```

## 架构

- `MyGestures`：WPF 应用、Windows 鼠标钩子、手势识别、输入模拟、轨迹提示、托盘与本地配置。
- `MyGestures/Web`：Vue 设置页。构建结果随应用发布，WebView2 加载本地资源。
- Web 页通过一个 `chrome.webview.postMessage` 桥直接访问同进程 .NET 服务。没有 Node 后端、插件总线、端口监听或 MyTools IPC 转发。
- 原生文案使用 RESX；Web 文案使用 JSON/i18next。两者均支持英语、简体中文与法语。

## 配置迁移

配置独立保存在 `%AppData%/MyGestures/Configuration.json`。首次运行时读取 `%AppData%/MyTools.Desktop/Gestures.json` 和 `Settings.json` 中的手势启用状态，不修改旧文件。后续只读取自己的配置；已有文件损坏时报告错误，不覆盖用户数据。

MyTools 的搜索、插件与账号设置保留。MyGestures 当前使用本地配置，未接入 MyTools 的账号同步。默认动作名称以首次创建时的语言写入，之后作为可编辑的用户数据保存。

MyTools 的正在运行的旧进程仍可能启用旧手势逻辑，更新后重新启动 MyTools 即可使用拆分后的版本。

## 贡献

欢迎提交 Issue 和 Pull Request。提交改动前，请确保解决方案能够成功构建，并运行与改动相关的测试。
