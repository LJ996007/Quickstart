# Quickstart macOS MVP

## 目标
- Windows 版继续保留在 `Quickstart/`，不改现有 `dotnet` 构建链。
- macOS 新增原生 AppKit 菜单栏客户端，工程位于 `macos/QuickstartMac/QuickstartMac.xcodeproj`。
- 当前阶段覆盖：菜单栏常驻、搜索弹窗、收藏 CRUD、中文/拼音搜索、文件/文件夹/URL/文本操作、文件/文件夹拖放添加、`--add <path>` 启动参数、`quickstart://add-url` 协议、与 Windows `config.json` 兼容。

## 目录
- `macos/QuickstartMac/QuickstartMac/`
  Swift 源码与 `Info.plist`
- `macos/QuickstartMac/QuickstartMacTests/`
  配置兼容、搜索、URL 协议单元测试
- `fixtures/windows-config-sample.json`
  Windows 样本配置，作为跨端契约基线

## JSON 契约
- 字段保持与 Windows `AppConfig` / `QuickEntry` 一致：
  `entries`、`groupLastUsedAt`、`totalCommanderPath`、`directoryOpusPath`、`defaultOpenWith`、`startWithWindows`、`shellMenuEnabled`、`hotKey`
- `EntryType` 与 `OpenWith` 按整数编码，兼容 `System.Text.Json` 默认 enum 数值写法。
- 日期按 .NET `DateTime` 的 ISO 8601 形式处理：
  `yyyy-MM-ddTHH:mm:ss.fffffffK`
- mac 端不会在 UI 中暴露 Windows 专属设置，但会读写并保留这些字段。

## 协议与配置位置
- URL 协议：
  `quickstart://add-url?url=<encoded>&title=<encoded>`
- 浏览器书签脚本与 Windows 保持一致。
- macOS 配置文件路径：
  `~/Library/Application Support/Quickstart/config.json`

## 当前取舍
- 已做：
  `NSStatusItem` 菜单栏入口
  `NSPanel` 搜索弹窗
  `NSWorkspace` 打开/定位
  `NSPasteboard` 复制文本和网址
  `CFStringTransform` 拼音搜索
- 暂不做：
  Finder 右键 / Quick Action
  开机启动
  全局右键拖拽手势
  自动签名身份管理
  第三方文件管理器集成

## 编译 macOS Swift 版

当前机器只有 Xcode Command Line Tools 时，也可以直接编译 AppKit 版本：

```bash
scripts/build-macos.sh
```

产物：
- `.app`：`macos/QuickstartMac/build/Release/QuickstartMac.app`
- zip：`macos/QuickstartMac/build/Release/Quickstart-macOS.zip`

脚本会通过 `xcrun` 使用同一套 macOS SDK 与 Swift 工具链编译、生成 `.app` bundle、替换 `Info.plist` 变量，并做本机 ad-hoc codesign。默认编译当前架构；如需指定架构：

```bash
ARCHS="arm64" scripts/build-macos.sh
ARCHS="arm64 x86_64" scripts/build-macos.sh
```

## 构建 DMG

```bash
scripts/build-macos-dmg.sh
```

产物：
- DMG：`macos/QuickstartMac/build/Release/Quickstart-macOS.dmg`

默认 DMG 内的 `.app` 是 ad-hoc 签名，只适合本机开发验证。分发给其它 Mac 时需要 Developer ID 签名和 Apple 公证，否则 Gatekeeper 可能提示“无法验证开发者”或“已损坏，无法打开”：

```bash
APP_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" \
DMG_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" \
NOTARY_PROFILE="notarytool-profile" \
scripts/build-macos-dmg.sh
```

`NOTARY_PROFILE` 需要提前用 `xcrun notarytool store-credentials` 写入钥匙串。应用会显示 Dock 图标，并常驻在 macOS 顶部菜单栏；点击 Dock 图标或顶部菜单栏的 `Quickstart` 都会打开主界面。

## 在 Xcode 中打开
```bash
open macos/QuickstartMac/QuickstartMac.xcodeproj
```

首次运行前建议确认：
- 使用 Xcode 15+/macOS 14+ 工具链
- 目标 Bundle Identifier 与本机签名策略匹配
- 以 Debug 本地运行验证 `quickstart://` 协议、`--add <path>`、拖放添加和配置写入路径
