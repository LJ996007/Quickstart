# Unreleased

> 自 **上一个已发布版本** 以来的改动先记在这里。
> **默认不升版本号**。只有明确要求「升版本 / bump version」时，才把本文件条目汇总进新版本说明并清空。

## 变更

- （暂无）

## 说明

- 修 bug、加功能、改代码：往「变更」里追加一条，**不要**改 `Quickstart/Quickstart.csproj` 的 `<Version>`。
- 纯文档 / 脚本 / 仓库整理：可记一条，也可不记。
- 升版本时由 `scripts/bump-version.ps1` 读取本文件，写入 `AppReleaseNotes.cs` 并重置本文件。
