# Quickstart

Quickstart 是一个快捷启动工具仓库，目前包含：

- `Quickstart/`：Windows WinForms 系统托盘版本
- `macos/QuickstartMac/`：macOS AppKit 菜单栏 MVP 版本

程序基于 WinForms 开发，常驻系统托盘，支持中文名称的子串搜索和拼音首字母搜索，并可与 Total Commander、Directory Opus、Windows 资源管理器以及右键菜单集成。

## 主要功能

- 托盘常驻，点击托盘图标即可呼出搜索弹窗
- 收藏常用文件夹和文件，支持快速新增、编辑、删除
- 支持中文关键字和拼音首字母搜索
- 支持通过 Total Commander、Directory Opus 或资源管理器打开目录
- 支持文件和目录右键菜单“添加到 Quickstart”
- 支持单实例运行，重复启动会唤起已运行实例
- 首次运行可自动检测 Total Commander 和 Directory Opus 路径
- 配置保存在当前用户目录，不依赖外部数据库

## 运行环境

- Windows
- .NET 10.0 SDK（开发和本地构建时需要）

## macOS MVP

macOS 版本是新增的原生 AppKit 工程，入口位于：

```text
macos/QuickstartMac/QuickstartMac.xcodeproj
```

更多兼容策略、配置契约和当前阶段取舍见：

```text
docs/macos-port.md
```

## 快速开始

### 构建

```bash
dotnet build Quickstart.sln
```

### 运行

```bash
dotnet run --project Quickstart/Quickstart.csproj
```

### 发布

```bash
dotnet publish Quickstart/Quickstart.csproj -c Release -r win-x64
```

发布后可执行文件默认位于：

```text
Quickstart/bin/Release/net10.0-windows/win-x64/publish/Quickstart.exe
```

这个 `Quickstart.exe` 已经是 `64` 位、自包含、单文件版本，可以直接发给别人双击运行，不需要额外安装 .NET。

### 生成安装版 EXE

如果你希望别人通过“下一步、下一步”的方式安装，并自动创建开始菜单、桌面快捷方式和卸载入口，可以使用仓库内置的 Inno Setup 脚本。

1. 先安装 `Inno Setup 6`
2. 执行：

```powershell
.\scripts\build-installer.ps1
```

生成后的安装包位于：

```text
artifacts/installer/Quickstart-Setup-x64.exe
```

说明：

- 安装包为 `64` 位安装程序
- 默认安装到当前用户目录 `%LOCALAPPDATA%\Programs\Quickstart`
- 不需要管理员权限
- 安装完成后可直接启动，并带卸载入口

### 正式发布流程

仓库现在提供了一个正式发布流程，会自动：

- 从 `Quickstart.csproj` 读取版本号
- 生成带版本号的发布目录
- 同时产出便携版和安装版
- 生成 `SHA256SUMS.txt` 和 `release-info.txt`
- 支持双击 `release.cmd` 一键出包

完整发布安装包前，仍然需要先安装 `Inno Setup 6`。

命令行方式：

```powershell
.\scripts\build-release.ps1
```

双击方式：

```text
release.cmd
```

发布目录结构示例：

```text
artifacts/
  releases/
    v1.0.0/
      win-x64/
        Quickstart-v1.0.0-win-x64.exe
        SHA256SUMS.txt
        release-info.txt
        publish/
          Quickstart.exe
        installer/
          Quickstart-Setup-v1.0.0-win-x64.exe
        symbols/
          Quickstart-v1.0.0-win-x64.pdb
```

说明：

- `publish/` 是原始发布目录，适合便携分发或排查问题
- 根目录下的 `Quickstart-v<version>-win-x64.exe` 是便于直接分发的版本化副本
- 安装包文件名也会自动带版本号
- 如果只想验证便携版发布，不生成安装包，可以执行：

```powershell
.\scripts\build-release.ps1 -SkipInstaller
```

## 使用说明

### 基本操作

1. 启动程序后，应用会驻留在系统托盘。
2. 点击托盘图标，打开主搜索窗口。
3. 输入关键字或拼音首字母筛选收藏项。
4. 选择条目后打开对应文件或目录。

### 添加条目

可以通过以下方式添加收藏：

- 在程序主界面中手动新增
- 对文件或文件夹使用右键菜单“添加到 Quickstart”
- 启动程序时传入命令行参数

命令行参数：

```bash
Quickstart.exe --add "C:\Path\To\Target"
```

如果程序已经在运行，新启动实例会通过命名管道把参数转发给现有实例，不会重复打开多个程序进程。

### 设置项

设置窗口支持：

- 配置 Total Commander 路径
- 配置 Directory Opus 路径
- 选择默认目录打开方式
- 设置是否开机启动
- 设置是否启用右键菜单集成

## 配置文件

配置文件默认保存在：

```text
%LOCALAPPDATA%\Quickstart\config.json
```

配置内容包括收藏条目、外部文件管理器路径、默认打开方式、开机启动和右键菜单状态等。

## 项目结构

```text
Quickstart/
├── Program.cs
├── Core/
│   ├── AppConfig.cs
│   ├── AppConfigJsonContext.cs
│   ├── ConfigManager.cs
│   ├── DopusDetector.cs
│   ├── ProcessLauncher.cs
│   ├── ShellIntegration.cs
│   ├── SingleInstance.cs
│   └── TcDetector.cs
├── Models/
│   └── QuickEntry.cs
├── UI/
│   ├── EntryEditForm.cs
│   ├── MainPopup.cs
│   ├── SettingsForm.cs
│   └── TrayIcon.cs
└── Utils/
    ├── ButtonStyler.cs
    ├── IconExtractor.cs
    └── PinyinHelper.cs
```

## 实现说明

- 使用 Mutex 和命名管道保证单实例运行，并处理二次启动参数转发
- 使用 System.Text.Json 源生成进行配置序列化
- 配置写入采用临时文件加替换方式，降低写坏配置的风险
- 通过注册表在当前用户范围内注册资源管理器右键菜单
- 发布配置为单文件、自包含、裁剪发布

## 说明

- 当前仓库没有单独的测试项目
- 项目未依赖外部 NuGet 包
- 程序界面文本为中文
