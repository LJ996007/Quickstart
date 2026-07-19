# 版本与发布规则

## 核心原则

1. **默认不升版本号**。日常改代码、修 bug、加功能都先记到 [`UNRELEASED.md`](./UNRELEASED.md)，**不要**每次改动都动 `<Version>`。
2. **只有明确要求升版本**（例如「升版本」「bump 到 1.0.14」）时才升号。
3. **升版本时**：把自上一个版本以来的**全部**未发布变更汇总进**新版本**的更新说明（应用内 `AppReleaseNotes` + GitHub Release 正文）。
4. **升完版本后**：必须提醒同步 GitHub。
5. **同步 GitHub（升版本后）**：提交推送代码，并 **编译 Release**，把便携版 / 安装包上传到 GitHub Release。

## 版本号来源

| 位置 | 作用 |
|------|------|
| `Quickstart/Quickstart.csproj` → `<Version>` | 程序集版本（权威来源） |
| `Quickstart/Core/AppReleaseNotes.cs` | 应用内「程序信息」更新说明 |
| `installer/Quickstart.iss` | 安装包默认版本（构建脚本会用 csproj 覆盖） |
| `README.md` 徽章与文案 | 展示当前版本 |
| `docs/UNRELEASED.md` | 尚未归入版本号的变更池 |

## 日常开发

```text
改代码
  → 把改动要点追加到 docs/UNRELEASED.md
  → 不改 Version
  → 需要的话照常 commit / push（只是代码同步，不是发版）
```

## 升版本

```powershell
# 默认 patch +1（1.0.13 → 1.0.14），日期今天
.\scriptsump-version.ps1

# 指定版本
.\scriptsump-version.ps1 -Version 1.1.0

# 额外再塞几条说明（可选）
.\scriptsump-version.ps1 -Notes "修复某某","优化某某"
```

或：

```bat
bump-version.cmd
```

脚本会：

1. 读取 csproj 当前版本与 `UNRELEASED.md` 条目
2. 在 `AppReleaseNotes.cs` **最前面**插入新版本块（包含自上版以来全部变更）
3. 更新 csproj / iss 默认版本 / README 展示
4. 重置 `UNRELEASED.md`
5. 写出 `artifacts/release/vX.Y.Z/release-notes.md` 草稿
6. 打印提醒：请同步 GitHub（会带 Release 打包上传）

## 同步 GitHub 并发布

升版本后（或已有新版本待发布）：

```powershell
# 提交当前改动 + push + 打 Release 包 + 创建/更新 GitHub Release 并上传产物
.\scripts\sync-github.ps1

# 只要推代码、不发 GitHub Release
.\scripts\sync-github.ps1 -SkipRelease

# 自定义提交说明
.\scripts\sync-github.ps1 -Message "release: v1.0.14"

# 跳过安装包（只上传便携 exe）
.\scripts\sync-github.ps1 -SkipInstaller
```

或：

```bat
sync-github.cmd
```

### 发布产物

来自 `release.cmd` / `scripts/build-release.ps1`：

```text
artifacts/release/vX.Y.Z/win-x64/
  Quickstart-vX.Y.Z-win-x64.exe
  publish/Quickstart.exe
  installer/Quickstart-Setup-vX.Y.Z-win-x64.exe   (若未 SkipInstaller 且本机有 Inno Setup)
  SHA256SUMS.txt
  release-info.txt
```

GitHub Release 会附带便携版 exe、校验文件，以及（若存在）安装包。

## 给协作者 / AI 的一句话

> 没叫你升版本就别动版本号；叫你升版本就把 UNRELEASED 全部写进新版本说明，升完提醒同步；同步发版时要编译 release 并上传 GitHub Release。
