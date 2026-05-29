# Quickstart macOS（Avalonia / .NET 版）

为解决「Windows(C#) 与 macOS(Swift) 双份实现、mac 端永远追不上」的问题，macOS 端改用
**Avalonia(.NET)**，复用与 Windows 相同的 C# 业务逻辑。Windows 端继续保留 WinForms。

## 工程结构
- `Quickstart.Core/`（net10.0，无 UI / 无平台依赖）—— 两端共享：
  配置、`ConfigManager`、拼音、`PromptRenderer`、`SkillRunner`、`AiClient`、
  `AiSecretStore` + `ISecretProtector` 抽象、`EntryQueries`、`QuickstartProtocol`。
- `Quickstart/`（net10.0-windows，WinForms）—— 现有 Windows 端，引用 Core。
- `Quickstart.Mac/`（net10.0，Avalonia）—— macOS 客户端，引用 Core；也能在 Windows 上
  运行以调试跨平台 UI。

## 已迁移功能
托盘/菜单栏常驻、主弹窗（搜索 / 文件夹·文件·网页·文本四类标签 / 分组 / 列表 / 图标）、
拼音搜索、条目增删改、打开/复制、AI 面板（Prompt/Skill）、全局右键拖拽手势
（右滑→主弹窗，左滑→捕获选区并打开 AI）、列表 favicon + 自定义图标、Keychain 密钥。

## 平台相关实现
- 密钥：Windows = DPAPI（`DpapiSecretProtector`）；macOS = Keychain（`MacSecretProtector`，经 `security`）。
- 手势/捕获：macOS = `CGEventTap` + 合成 Cmd+C（`Quickstart.Mac/Platform/`），门控 `OperatingSystem.IsMacOS()`。
  需在「系统设置 → 隐私与安全性 → 辅助功能 / 输入监控」授权后生效。

## 在 Mac 上运行（开发）
```bash
dotnet run --project Quickstart.Mac
```

## 打包 .app / .dmg（在 Mac 上）
```bash
# 生成 Quickstart.Mac/build/Quickstart.app（默认 osx-arm64；Intel 用 RID=osx-x64）
scripts/build-mac-app.sh
RID=osx-x64 scripts/build-mac-app.sh

# 生成 Quickstart.Mac/build/Quickstart-macOS.dmg
scripts/build-mac-dmg.sh
```
脚本做自包含发布 + 组装 `.app` bundle（Info.plist、可选 .icns、`quickstart://` 协议注册）+ ad-hoc 签名。
分发到其它 Mac 需 Developer ID 签名 + 公证，否则 Gatekeeper 会拦截。

## 与旧 Swift 版的关系
`macos/QuickstartMac/`（AppKit/Swift MVP）功能停留在早期阶段，迁移完成后将归档/移除，
以 Avalonia 版为准。

## 待办
开机启动（LaunchAgent）+ mac 设置窗口、Finder 中定位、`quickstart://` 协议在 mac 上的处理、
Developer ID 签名/公证流程。
