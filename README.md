# MyGestures

独立运行的 Windows 鼠标手势应用，从 MyTools 抽离。无需启动 MyTools，也无需 Node.js 运行时。

## 构建与运行

需要 .NET SDK 和用于构建前端的 Node.js/npm。运行时需要 Windows 与 WebView2 Runtime。

```powershell
cd D:\repos\MyGestures\MyGestures\Web
npm ci
npm run build
cd D:\repos\MyGestures
dotnet build MyGestures/MyGestures.csproj -p:OutputPath=bin/AgentVerification/
dotnet test MyGestures.Test/MyGestures.Test.csproj -p:OutputPath=bin/AgentVerification/
& ./MyGestures/bin/AgentVerification/MyGestures.exe
```

启动时显示设置页。关闭设置窗口后继续在托盘运行，双击托盘图标重新打开设置，从托盘菜单退出。`--background` 可仅启动托盘，不显示设置。

设置修改后自动保存，也可点击保存按钮立即保存或重试失败的操作。可编辑手势名称、方向、目标进程、键盘快捷键或鼠标动作，以及各项启用状态。目标进程为空时全局生效；同一手势的进程专属配置优先。录制手势或动作时暂时暂停检测。

## 架构

- `MyGestures`：WPF 应用、Windows 鼠标钩子、手势识别、输入模拟、轨迹提示、托盘与本地配置。
- `MyGestures/Web`：Vue 设置页。构建结果随应用发布，WebView2 加载本地资源。
- Web 页通过一个 `chrome.webview.postMessage` 桥直接访问同进程 .NET 服务。没有 Node 后端、插件总线、端口监听或 MyTools IPC 转发。
- 原生文案使用 RESX；Web 文案使用 JSON/i18next。两者均支持英语、简体中文与法语。

## 配置迁移

配置独立保存在 `%AppData%/MyGestures/Configuration.json`。首次运行时读取 `%AppData%/MyTools.Desktop/Gestures.json` 和 `Settings.json` 中的手势启用状态，不修改旧文件。后续只读取自己的配置；已有文件损坏时报告错误，不覆盖用户数据。

MyTools 的搜索、插件与账号设置保留。MyGestures 当前使用本地配置，未接入 MyTools 的账号同步。默认动作名称以首次创建时的语言写入，之后作为可编辑的用户数据保存。

MyTools 的正在运行的旧进程仍可能启用旧手势逻辑，更新后重新启动 MyTools 即可使用拆分后的版本。
