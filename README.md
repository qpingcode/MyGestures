# MyGestures

[English](README.md) | [简体中文](README.zh-CN.md)

[![Build and release](https://github.com/qpingcode/MyGestures/actions/workflows/release.yml/badge.svg)](https://github.com/qpingcode/MyGestures/actions/workflows/release.yml)

MyGestures is a standalone Windows mouse-gesture application extracted from MyTools. It does not require MyTools or a Node.js runtime.

## Features

- Hold the right mouse button and draw a gesture to run the assigned action.
- Continue running from the system tray after the settings window is closed. Double-click the tray icon to reopen settings, or exit from the tray menu. `--background` starts in the tray only.
- Edit gesture names, directions, target processes, keyboard shortcuts, mouse actions, and whether each item is enabled.
- An empty target process makes a gesture global. A process-specific gesture takes priority over the global one.
- Recording a gesture or action pauses detection until you finish.
- Settings save automatically.

## System Requirements

- Windows 10 or Windows 11, x64.
- WebView2 Runtime, which is usually already installed.
- The Full release is self-contained and does not require a separate .NET Desktop Runtime.
- The Lite release requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- Building from source requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Node.js/npm.

## Installation

MyGestures is published as a Full build (bundled .NET runtime) and a Lite build (requires .NET 8 Desktop Runtime). Each build stays on its own update track.

<!-- mygestures-downloads:start -->
| Type | Channel | Version | Installer | Portable |
| --- | --- | --- | --- | --- |
| **Full** | **Stable** | — | Not published yet | Not published yet |
| **Lite** | **Stable** | — | Not published yet | Not published yet |
| **Full** | **Beta** | 0.0.3 | [Download](https://github.com/qpingcode/MyGestures/releases/download/v0.0.3/MyGestures-0.0.3-windows-x64-full-setup.exe) | [Download](https://github.com/qpingcode/MyGestures/releases/download/v0.0.3/MyGestures-0.0.3-windows-x64-full-portable.zip) |
| **Lite** | **Beta** | 0.0.3 | [Download](https://github.com/qpingcode/MyGestures/releases/download/v0.0.3/MyGestures-0.0.3-windows-x64-lite-setup.exe) | [Download](https://github.com/qpingcode/MyGestures/releases/download/v0.0.3/MyGestures-0.0.3-windows-x64-lite-portable.zip) |
<!-- mygestures-downloads:end -->




Stable is the recommended channel. Push a git tag named `release-YYYY-MM-DD` (for example `release-2026-09-13`) to publish a stable build. Every push to `main` publishes a beta.

A portable package can be extracted and run as `MyGestures.exe`.

> Authenticode signing is not yet enabled. Windows SmartScreen may display an unknown publisher warning, so download MyGestures only from this repository's Releases page.

## Build from source

```powershell
cd MyGestures/Web
npm ci
npm run build
cd ../..
dotnet build MyGestures/MyGestures.csproj -p:OutputPath=bin/AgentVerification/
dotnet test MyGestures.Test/MyGestures.Test.csproj -p:OutputPath=bin/AgentVerification/
./MyGestures/bin/AgentVerification/MyGestures.exe
```

## Architecture

- `MyGestures`: WPF host, Windows mouse hook, gesture recognition, input simulation, trail overlay, tray icon, and local configuration.
- `MyGestures/Web`: Vue settings page. The build output ships with the app and is loaded by WebView2 from local files.
- The web page talks to in-process .NET services through `chrome.webview.postMessage`. There is no Node backend, plugin bus, local port, or MyTools IPC forwarding.
- Native strings use RESX. Web strings use JSON/i18next. Both support English, Simplified Chinese, and French.

## Configuration migration

Configuration is stored at `%AppData%/MyGestures/Configuration.json`. On first run, MyGestures reads gesture enablement from `%AppData%/MyTools.Desktop/Gestures.json` and `Settings.json` without modifying those files. After that it only reads its own configuration. A damaged file is reported as an error and is not overwritten.

MyTools search, plugin, and account settings stay in MyTools. MyGestures currently uses local configuration and does not sync through a MyTools account. Default action names are written in the language used when they are first created, then stored as editable user data.

An already-running older MyTools process may still use the previous gesture implementation. Restart MyTools after updating so the split version takes effect.

## Contributing

Issues and pull requests are welcome. Before submitting a change, make sure the solution builds successfully and run the tests relevant to your changes.
