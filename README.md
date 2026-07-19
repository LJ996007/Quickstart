# Quickstart

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![Version](https://img.shields.io/badge/version-1.0.13-green)
![License](https://img.shields.io/github/license/LJ996007/Quickstart)
![Release](https://img.shields.io/github/v/release/LJ996007/Quickstart)

Quickstart 是一个常驻系统托盘 / 菜单栏的快捷启动与效率工具。

它不只是收藏夹：把**常用路径、网页、固定文本**收在一起，再用**右键拖动手势**在任意窗口秒级呼出。Windows 版还把 **AI 动作、Everything 搜索、截图 OCR、剪贴板历史、粘贴为纯文本** 接进同一套左滑手势里，尽量少点几下就能干完事。

当前主线版本：**Windows 1.0.13**（.NET 10 / WinForms）。macOS 以 **Avalonia + 共享 Core** 推进；仓库里仍保留早期 Swift/AppKit 原型。

---

## 快速开始

### Windows

1. 从 [Releases](https://github.com/LJ996007/Quickstart/releases) 下载最新 `Quickstart.exe`（单文件、自包含，一般无需安装）。
2. 双击运行，程序出现在系统托盘。
3. **左键**点托盘图标打开主界面；或按住**鼠标右键向右拖**直接呼出启动器。

> **环境**：Windows 10/11。发布版已自带运行时，无需再装 .NET。  
> 调试可用环境变量 `QUICKSTART_PERF=1`，启动打点会写到 `%LOCALAPPDATA%\Quickstart\startup-trace.log`。

### macOS

仓库内有两套实现：

| 实现 | 路径 | 状态 |
|------|------|------|
| **Avalonia / .NET（推荐）** | `Quickstart.Mac/` | 与 Windows 共享 `Quickstart.Core`，持续跟进 |
| Swift / AppKit（早期） | `macos/QuickstartMac/` | 功能停留在早期 MVP，迁移完成后计划归档 |

**Avalonia 开发运行（在 Mac 上）：**

```bash
dotnet run --project Quickstart.Mac
```

**打包 .app / .dmg：**

```bash
scripts/build-mac-app.sh          # 默认 osx-arm64；Intel: RID=osx-x64
scripts/build-mac-dmg.sh
```

全局右键拖拽需要在「系统设置 → 隐私与安全性」中授权**辅助功能 / 输入监控**。未签名包首次打开可能被 Gatekeeper 拦截，本机可用右键「打开」，分发请走 Developer ID 签名与公证。

---

## 软件功能

### 1. 托盘常驻，多种呼出方式

- Windows：系统托盘；macOS：菜单栏 / Dock（视实现而定）。
- 点击托盘 / 菜单栏打开主弹窗。
- **全局快捷键**（设置里配置，例如 `Ctrl+Shift+Space`；留空禁用）。
- **右键拖动手势**（可开关，可调触发距离与垂直容差）：
  - **向右拖** → 快捷启动主弹窗（收藏 / 最近 / 剪贴板历史）
  - **向左拖** → 动作面板（AI Prompt、Skill、工具），或配置为直接 Everything 搜索选中文本
- 二次启动会唤起已运行实例；支持 `--add`、`quickstart://` 协议、`--settings`。

### 2. 一个入口管理四类收藏

- **文件夹**：工作目录、项目、下载、素材等。
- **文件**：文档、表格、脚本、exe 等。
- **网页**：后台、文档、搜索入口；可抓取 favicon，也支持自定义图标。
- **文本**：邮箱、命令片段、模板回复、提示词等，点击即复制。

主界面按类型分栏，右侧按**分组**筛选；支持中文子串 + **拼音首字母**搜索。

### 3. Windows 主弹窗不止「收藏」

右滑主弹窗（Windows）常见标签包括：

| 标签 | 内容 |
|------|------|
| 文件夹 / 文件 / 网页 / 文本 | 你的收藏条目 |
| **最近** | 读取 Windows「最近使用」快捷方式（文档为主；资源管理器纯浏览文件夹不一定进系统 Recent） |
| **历史** | 系统**剪贴板文本历史**：自动入队、去重、可选落盘；点选或手势松手 → 以纯文本再次写入剪贴板 |

列表可「记住上次标签和分组」、可按最近使用排序。

### 4. 按类型做最合适的动作

- 文件夹：Total Commander / 资源管理器 / Directory Opus（可设默认，右键可临时换；Opus 走 `dopusrt` 在现有窗口新标签打开）。
- 文件：系统默认程序打开；可在资源管理器中定位。
- 网页：浏览器打开 / 复制网址。
- 文本 / 剪贴板历史：复制为纯文本并提示「已复制」。

### 5. 低成本添加条目

- 主界面手动新增、编辑。
- 文件/文件夹**拖进**主界面。
- Windows 资源管理器右键「添加到 Quickstart」（可关）。
- 命令行：`Quickstart.exe --add <path>`（单实例会转发给已运行进程）。
- 浏览器**一键添加书签脚本** + 自定义协议 `quickstart://add-url?...`。

### 6. 左滑动作面板（Windows 重点）

按住右键**向左拖**，默认弹出紧凑双栏动作面板（内容自适应高度，带「最近使用」）：

**Prompt / Skill（AI）**

- 多服务商（默认预置 DeepSeek / Qwen / GLM 等 OpenAI 兼容接口）。
- 自定义 Prompt 模板（`{文本}` 占位）；目标可以是：
  - **API**：在 AI 面板内流式/展示结果；
  - **网页**：把渲染后的提示词送到配置的网页对话页（如 DeepSeek 网页版）并尽力自动粘贴。
- Skill：多步 Prompt 流水线。
- 可从当前前台窗口捕获选区文本，或读取选中文件内容（有大小上限）。
- API Key 等敏感信息走 **DPAPI** 保护存储，不进明文 `config.json`。

**工具列（可扩展）**

- **粘贴为纯文本**：清洗剪贴板并 Ctrl+V 回手势来源窗口（会去掉末尾多余换行）。
- **Everything 搜索**：用选中文本调用本机 Everything（路径可自动探测）。
- **截图 OCR**：框选屏幕区域 → 百度通用文字识别 → 纯文本进剪贴板（密钥同样加密保存）。
- **网页查询工具**：可配置 URL 模板（默认含 Google、政府采购网 site 搜、信用中国 site 搜等），用选中文本打开浏览器。

设置里可将「向左动作」改成：**不弹面板，松手直接 Everything 搜索**。

### 7. 剪贴板历史（Windows）

- 后台监听系统剪贴板文本变化，去重、截断超长内容。
- 右滑主弹窗 → **历史** 标签查看；点击/手势命中即再次复制为纯文本。
- 设置：**启用**、**最多条数**、**退出后是否落盘**、**清空历史**。  
  落盘路径：`%LOCALAPPDATA%\Quickstart\clipboard-history.json`（与主配置分离）。

### 8. 截图 OCR（Windows）

1. 在[百度智能云](https://console.bce.baidu.com/)创建文字识别应用，取得 API Key / Secret Key。  
2. Quickstart **设置 → 截图 OCR** 填入并启用。  
3. 左滑 → **截图 OCR** → 拖拽框选 → 识别成功后托盘提示，直接 Ctrl+V。

图片会上传至百度 OCR 服务；取消框选不会发请求。免费额度以百度控制台为准。

### 9. 设置页结构（Windows）

| 导航 | 内容 |
|------|------|
| 常规 | TC / DOpus / Everything 路径与检测、默认打开方式、开机启动、资源管理器右键菜单 |
| 快捷启动 | 全局快捷键、右键拖动手势开关、向左动作、触发距离/垂直容差 |
| 列表与工具 | 记住视图、最近优先、网页查询工具管理、书签脚本、协议修复 |
| AI | 进入模型 / Prompt / Skill 配置 |
| 截图 OCR | 开关与百度 AK/SK |
| 剪贴板 | 历史开关、条数、落盘、清空 |
| 程序信息 | 当前版本与应用内更新说明 |

### 10. 使用痕迹

条目与分组会记录最近使用时间，常用的会更容易排到前面（可按设置决定是否按最近使用排序）。

---

## 适合的使用场景

- 多项目目录、脚本目录、交付目录统一入口，并用 TC / DOpus / 资源管理器打开。
- 后台地址、接口文档、测试环境一键打开或浏览器书签丢进收藏。
- 固定话术 / 命令 / 提示词一键复制；剪贴板历史找回刚复制过的纯文本。
- 任意软件里选中文字 → 左滑总结/翻译/解释，或丢给 Everything / 网页站内搜。
- 屏幕上不能选的字 → 截图 OCR 后粘贴。
- 富文本复制后只要纯文本 → 左滑「粘贴为纯文本」。

---

## 仓库结构

```text
Quickstart.sln
├── Quickstart/                 # Windows 客户端（WinForms, net10.0-windows）
│   ├── Program.cs              # 单实例、手势、热键、托盘、IPC
│   ├── Core/                   # 平台侧服务：钩子、剪贴板、OCR 流程、Shell…
│   ├── UI/                     # MainPopup、动作面板、AI、设置等
│   └── Utils/                  # 图标、双缓冲控件、DPI 等
├── Quickstart.Core/            # 跨平台业务：配置、拼音、AI 客户端、协议…
├── Quickstart.Mac/             # macOS Avalonia 客户端（共享 Core）
├── macos/QuickstartMac/        # 早期 Swift/AppKit 原型（遗留）
├── scripts/                    # macOS 构建 / DMG 脚本
├── installer/                  # Windows Inno Setup
├── docs/                       # 设计与实现笔记（手势、OCR、剪贴板等）
├── debug.cmd / release.cmd     # Windows 本地构建
└── artifacts/                  # 统一构建输出目录
```

配置目录（Windows）：`%LOCALAPPDATA%\Quickstart\`  
主要文件：`config.json`、可选 `clipboard-history.json`、图标缓存、密钥保护数据等。

---

## 本地构建

### Windows

```bash
debug.cmd
release.cmd -SkipInstaller
```

或：

```bash
dotnet build Quickstart.sln
dotnet run --project Quickstart/Quickstart.csproj
dotnet publish Quickstart/Quickstart.csproj -c Release -r win-x64
```

产物约定在 `artifacts/`：

```text
artifacts/
├── debug/win-x64/              # 可直接跑的 Debug
├── release/vX.Y.Z/win-x64/     # 正式发布物与校验文件
└── obj/                        # 中间文件
```

`release.cmd` 默认尝试打安装包；只要便携版加 `-SkipInstaller`。

### macOS（Avalonia）

```bash
dotnet run --project Quickstart.Mac
scripts/build-mac-app.sh
scripts/build-mac-dmg.sh
```

### macOS（遗留 Swift 原型）

```bash
scripts/build-macos.sh
ARCHS="arm64 x86_64" scripts/build-macos.sh
scripts/build-macos-dmg.sh
```

本地多为 ad-hoc 签名，仅适合本机验证。

---

## 命令行与协议

| 用法 | 说明 |
|------|------|
| `Quickstart.exe` | 启动或唤起已运行实例主界面 |
| `Quickstart.exe --add <path>` | 添加文件/文件夹（右键菜单 / IPC） |
| `Quickstart.exe --settings` | 打开设置 |
| `quickstart://add-url?...` | 浏览器书签脚本添加网页 |

---

## 版本与发布

- 当前 Windows 程序版本见 `Quickstart/Quickstart.csproj` 的 `<Version>`（现为 **1.0.13**）。
- 应用内「程序信息」页展示 `AppReleaseNotes` 中的更新列表。
- **默认不升版本号**：日常改动先记到 `docs/UNRELEASED.md`，不改 `<Version>`。
- **需要发版时**：运行 `bump-version.cmd`，会把 `UNRELEASED.md` 的全部条目汇总成新版本说明，更新 csproj / 安装脚本 / README，然后重置变更池。
- **升版后同步 GitHub**：运行 `sync-github.cmd`，自动提交推送、编译 Release、把便携版 / 安装包 / 校验文件上传到 GitHub Releases。
- 完整规则见 [`docs/versioning.md`](./docs/versioning.md)。

近期 Windows 侧已包含（摘要）：右滑「最近」与剪贴板历史、左滑 AI/工具面板与截图 OCR、Everything 与网页查询工具、全局热键与手势参数、Directory Opus 新标签打开修复、启动与鼠标钩子性能优化、设置页导航高亮与稳定性修复等。完整条目以应用内版本说明为准。

---

## 许可证

见仓库 [License](https://github.com/LJ996007/Quickstart) 徽章对应文件。
