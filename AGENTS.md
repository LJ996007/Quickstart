# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

Windows 系统托盘快捷启动工具（WinForms），支持收藏文件夹/文件的快速搜索与打开，集成 Total Commander 和 Windows 资源管理器右键菜单。

## Build & Run

```bash
# 构建
dotnet build Quickstart.sln

# 运行
dotnet run --project Quickstart/Quickstart.csproj

# 发布（单文件自包含）
dotnet publish Quickstart/Quickstart.csproj -c Release -r win-x64
```

无测试项目。目标框架为 `net10.0-windows`，需 .NET 10.0 SDK。

## Architecture

单项目 C# WinForms 应用，按职责分层：

```
Quickstart/
├── Program.cs              # 入口：单实例互斥、IPC 命名管道、GB2312 编码注册
├── Core/
│   ├── AppConfig.cs        # 配置模型 + OpenWith 枚举
│   ├── AppConfigJsonContext.cs  # 源码生成的 JSON 序列化
│   ├── ConfigManager.cs    # 线程安全配置 CRUD（原子写入 + 备份）
│   ├── ProcessLauncher.cs  # 通过 Explorer 或 TC 打开路径
│   ├── ShellIntegration.cs # 注册表右键菜单集成（HKCU）
│   ├── SingleInstance.cs   # Mutex + NamedPipe IPC
│   └── TcDetector.cs       # 自动检测 Total Commander 安装路径
├── Models/
│   └── QuickEntry.cs       # 收藏条目数据模型（Id/Name/Path/Type/Group 等）
├── UI/
│   ├── TrayIcon.cs         # 系统托盘图标 + 右键菜单
│   ├── MainPopup.cs        # 无边框搜索弹窗（拼音首字母过滤、拖放、键盘导航）
│   ├── SettingsForm.cs     # 设置对话框（TC 路径/默认打开方式/开机启动）
│   └── EntryEditForm.cs    # 条目编辑对话框
└── Utils/
    ├── IconExtractor.cs    # shell32.dll P/Invoke 提取文件图标（带缓存）
    └── PinyinHelper.cs     # GB2312 拼音首字母匹配
```

## Key Design Points

- **单实例**：`SingleInstance` 用 Mutex 防止多开，二次启动通过 NamedPipe 将 `--add <path>` 参数转发给已运行实例
- **拼音搜索**：`PinyinHelper` 将 GB2312 一级汉字映射到拼音首字母，`MainPopup` 同时支持子串匹配和拼音首字母匹配
- **配置持久化**：JSON 存储在 `%LOCALAPPDATA%\Quickstart\config.json`，`ConfigManager` 用 lock 保证线程安全，写入采用临时文件+重命名确保原子性
- **源码生成 JSON**：使用 `AppConfigJsonContext`（`System.Text.Json` 源生成器）以兼容 trimming
- **Shell 集成**：`ShellIntegration` 在注册表 `HKCU\Software\Classes\*\shell\Quickstart` 等位置写入右键菜单项
- **发布配置**：csproj 中配置了 PublishSingleFile + PublishTrimmed + SelfContained，InvariantGlobalization=false（GB2312 需要）

## Command-line Arguments

- `--add <path>` — 添加文件/文件夹到收藏列表（用于右键菜单 IPC 调用）

## Notes

- csproj 启用了 `AllowUnsafeBlocks`（IconExtractor P/Invoke 需要）和 `Nullable`
- 无外部 NuGet 依赖
- UI 文本全部为中文
