# Quickstart
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![Swift](https://img.shields.io/badge/Swift-macOS-orange)
![License](https://img.shields.io/github/license/LJ996007/Quickstart)
![Release](https://img.shields.io/github/v/release/LJ996007/Quickstart)

Quickstart 是一个常驻系统托盘的快捷启动工具，核心目标是把常用内容集中起来，通过尽量少的操作完成搜索、定位、打开和复用。

它不只适合收藏文件夹，也可以统一管理文件、网页和常用文本。你可以把它理解成一个面向日常高频动作的轻量入口：想找目录、开网页、复制一段固定文本，或者把当前手头的文件顺手收进去，都会更快。

## 快速开始

### Windows

1. 从 [Releases](https://github.com/LJ996007/Quickstart/releases) 页面下载最新的 `Quickstart.exe`（无需安装，单文件直接运行）。
2. 双击运行，程序会出现在系统托盘。
3. 左键点击托盘图标，即可打开主界面。

> **环境要求**：Windows 10/11，.NET 10.0 Runtime（单文件发布版已自包含，无需额外安装）

### macOS

当前仓库已包含 macOS Swift/AppKit 版本。

1. 下载或解压仓库内的 `macos/QuickstartMac/build/Release/Quickstart-macOS.zip`。
2. 双击 `QuickstartMac.app` 运行。
3. 程序会常驻在 macOS 顶部菜单栏，点击菜单栏图标即可打开主界面。

> **环境要求**：macOS 14+。当前打包产物是 Universal 版本，支持 Apple Silicon（arm64）和 Intel（x86_64）。

如果 macOS 首次运行提示“无法验证开发者”，可以在“系统设置 → 隐私与安全性”中允许打开，或在 Finder 中右键应用选择“打开”。当前包为本机 ad-hoc 签名，尚未做 Apple Developer ID 公证。

## 软件功能

### 1. 托盘常驻，随时呼出

- Windows 版启动后常驻在系统托盘；macOS 版启动后常驻在顶部菜单栏。
- 点击托盘/菜单栏图标即可打开主弹窗。
- 右键菜单提供设置和退出入口。
- Windows 版支持重复启动唤起已运行实例；macOS 版支持菜单栏常驻快速呼出。

### 2. 一个入口管理四类常用内容

Quickstart 可以收藏以下内容：

- 文件夹：作为常用工作目录、项目目录、下载目录、素材目录的快速入口。
- 文件：适合收藏文档、表格、脚本、可执行文件等固定文件。
- 网页：适合收藏后台地址、文档页面、搜索入口、系统面板等常用链接。
- 文本：适合保存邮箱、命令片段、账号备注、提示词、固定回复等高频复制内容。

主界面按“文件 / 网页 / 文本”分栏展示，不同类型分开管理，查找时更直接。

### 3. 中文搜索和拼音首字母搜索

- 支持按名称直接搜索。
- 支持中文内容的拼音首字母匹配。
- 既可以用完整关键字筛选，也可以用更短的输入快速定位目标。

这意味着像“项目文档”“客户资料”“下载目录”这类中文条目，不必完整输入中文，也能较快筛出来。

### 4. 分组管理，适合条目较多时使用

- 每个条目都可以设置分组。
- 主界面右侧会显示当前分类下的分组列表。
- 可以先切换分组，再在分组内搜索，减少干扰项。
- 分组支持按最近使用情况排序，常用分组会更靠前。

当收藏数量逐渐增多时，可以按项目、客户、用途、工作流阶段等方式拆分管理。

### 5. 按条目类型执行最合适的动作

- 文件夹：可按设定方式打开。
- 文件：直接用系统默认程序打开。
- 网页：直接在浏览器中打开。
- 文本：点击后直接复制到剪贴板，并显示“已复制”提示。

文本条目的定位不是“打开”，而是“立即可用”。对于经常要粘贴的内容会非常省事。

### 6. 文件夹支持多种打开方式

针对文件夹条目，Quickstart 在 Windows 版支持：

- 用 Total Commander 打开
- 用 Windows 资源管理器打开
- 用 Directory Opus 打开

macOS 版使用 Finder / 系统默认应用打开和定位文件。

你可以在 Windows 版设置里指定默认方式，也可以在条目的右键菜单里临时选择本次使用哪种方式打开。

### 7. 支持多种添加方式，尽量减少录入成本

Quickstart 不要求你手动一条条维护，常用内容可以从不同入口快速加入。

- 在主界面中手动新增和编辑条目。
- 直接把文件或文件夹拖到主界面中，快速加入收藏（Windows/macOS 均支持）。
- Windows 版可通过资源管理器右键菜单“添加到 Quickstart”把文件或目录送入程序。
- 通过命令行参数把外部路径添加进来，macOS 版支持 `--add <path>`。
- 对网页可通过浏览器书签脚本一键添加。

新增条目时可以填写名称、类型和分组；对于文件和文件夹，程序也会尽量帮你推断类型并带出默认名称。

### 8. 浏览器一键添加网页

除了手动添加网址外，Quickstart 还提供面向网页收藏的快捷方式：

- 设置页可以复制“一键添加网站”的书签脚本。
- 把这个书签保存到浏览器收藏栏后，在任意网页点一下即可把当前页面标题和网址传给 Quickstart。
- 程序内部通过自定义协议接收网页信息，适合快速沉淀后台链接、文档页和业务系统入口。

如果你经常在浏览器和本地工具之间切换，这个功能会比手动复制网址更顺手。

### 9. 键盘、鼠标和右键操作都比较完整

- 支持双击条目直接执行。
- 支持键盘导航与回车打开。
- 支持删除快捷操作。
- 不同类型条目会提供对应的右键菜单。

例如：

- 文件夹可以选择不同打开方式，或在资源管理器中定位。
- 网页可以直接打开，也可以复制网址。
- 文本可以直接复制，不需要额外点进编辑框。

### 10. 右键拖拽手势可直接呼出弹窗

除了点击托盘图标，Windows 版还支持全局鼠标手势呼出：

- 按住鼠标右键向右拖动，可以快速唤出主弹窗。
- 松开时可以直接选中并执行目标条目。

这个方式更接近“随手划出一个启动器”，适合希望尽量减少点击路径的使用习惯。macOS 版当前以菜单栏点击呼出为主。

### 11. 设置项围绕“日常使用效率”展开

Windows 版设置界面主要提供这些能力：

- 指定 Total Commander 路径
- 指定 Directory Opus 路径
- 选择默认文件夹打开方式
- 开启或关闭开机启动
- 开启或关闭资源管理器右键菜单集成
- 复制网页一键添加书签脚本
- 重新注册 Quickstart 自定义协议

macOS 版设置界面当前提供：

- 打开配置目录
- 复制一键添加网页的书签脚本
- 保留并兼容 Windows 配置字段

另外，Windows 版会在首次运行时尝试自动检测 Total Commander 和 Directory Opus，减少手动配置成本。

### 12. 使用痕迹会被记录，用起来会越来越顺手

- 条目会记录最近使用时间。
- 分组会记录最近使用时间。
- 常用内容会更容易被重新定位到。

它不是复杂的知识库，而是一个偏“高频动作加速器”的工具，所以这些细节会直接影响日常手感。

## 适合的使用场景

- 把多个项目目录、脚本目录、交付目录收在一起，作为统一入口。
- 收藏常开的系统后台、接口文档、测试地址、部署面板。
- 保存经常复制的文本片段，比如命令、模板回复、路径、账号说明、提示词。
- Windows 版可在 Total Commander、Directory Opus 和资源管理器之间自由切换目录打开方式；macOS 版可用 Finder / 默认应用打开和定位。
- 从右键菜单、拖放和浏览器快速沉淀新条目，而不是事后集中整理。

## macOS 版本说明

macOS 版本位于：

```bash
macos/QuickstartMac/
```

已支持：

- 原生 Swift/AppKit 菜单栏应用
- 菜单栏常驻与搜索弹窗
- 文件夹、文件、网页、文本四类条目
- 收藏新增、编辑、删除
- 中文搜索与拼音首字母搜索
- 文件/文件夹拖放添加
- `quickstart://add-url` 浏览器书签脚本添加网页
- `--add <path>` 从命令行添加文件或文件夹
- 与 Windows 版 `config.json` 字段兼容

当前暂未包含：

- Finder 右键菜单 / Quick Action
- 开机启动
- Apple Developer ID 正式签名与公证
- DMG 安装包

## 本地构建

### Windows

```bash
dotnet build Quickstart.sln
dotnet publish Quickstart/Quickstart.csproj -c Release -r win-x64
```

### macOS

macOS Swift 版可以用仓库脚本直接构建：

```bash
scripts/build-macos.sh
```

默认构建当前机器架构。构建 Universal 包：

```bash
ARCHS="arm64 x86_64" scripts/build-macos.sh
```

构建产物：

```bash
macos/QuickstartMac/build/Release/QuickstartMac.app
macos/QuickstartMac/build/Release/Quickstart-macOS.zip
```

如果安装了完整 Xcode，也可以打开工程：

```bash
open macos/QuickstartMac/QuickstartMac.xcodeproj
```
