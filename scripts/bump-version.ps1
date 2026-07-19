param(
    [string]$Version = "",
    [string[]]$Notes = @(),
    [string]$Date = "",
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$csproj = Get-Content -Path $ProjectPath -Encoding UTF8
    $propertyGroups = @($csproj.Project.PropertyGroup)
    $version = @($propertyGroups | Where-Object { $_.Version } | Select-Object -ExpandProperty Version -First 1)[0]
    if (-not [string]::IsNullOrWhiteSpace($version)) {
        return $version.Trim()
    }
    return "0.0.0"
}

function Set-ProjectVersion {
    param(
        [string]$ProjectPath,
        [string]$NewVersion
    )

    $content = Get-Content -Path $ProjectPath -Raw -Encoding UTF8
    if ($content -notmatch '<Version>[^<]+</Version>') {
        throw "Could not find <Version> in $ProjectPath"
    }
    $updated = [regex]::Replace($content, '<Version>[^<]+</Version>', "<Version>$NewVersion</Version>", 1)
    Set-Content -Path $ProjectPath -Value $updated -Encoding UTF8 -NoNewline
}

function Get-NextPatchVersion {
    param([string]$Current)

    $parts = $Current.Split('.')
    if ($parts.Count -lt 3) {
        throw "Version '$Current' is not in major.minor.patch form"
    }

    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]
    $patch++
    return "{0}.{1}.{2}" -f $major, $minor, $patch
}

function Get-UnreleasedNotes {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return @()
    }

    $lines = Get-Content -Path $Path -Encoding UTF8
    $inSection = $false
    $items = New-Object System.Collections.Generic.List[string]

    foreach ($line in $lines) {
        $trim = $line.Trim()
        if ($trim -match '^##\s+变更') {
            $inSection = $true
            continue
        }
        if ($inSection -and $trim -match '^##\s+') {
            break
        }
        if (-not $inSection) {
            continue
        }
        if ($trim -match '^-\s+(.+)$') {
            $text = $Matches[1].Trim()
            if ([string]::IsNullOrWhiteSpace($text)) { continue }
            if ($text -eq '（暂无）' -or $text -eq '(暂无)' -or $text -eq '暂无') { continue }
            $items.Add($text) | Out-Null
        }
    }

    return @($items)
}

function ConvertTo-CSharpString {
    param([string]$Value)
    return ($Value -replace '\\', '\\\\' -replace '"', '\"')
}

function Format-ReleaseNoteBlock {
    param(
        [string]$Version,
        [string]$DateValue,
        [string[]]$Items
    )

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('        new(')
    [void]$sb.AppendLine(('            "{0}",' -f $Version))
    [void]$sb.AppendLine(('            "{0}",' -f $DateValue))
    [void]$sb.AppendLine('            [')
    for ($i = 0; $i -lt $Items.Count; $i++) {
        $escaped = ConvertTo-CSharpString -Value $Items[$i]
        $comma = if ($i -lt $Items.Count - 1) { ',' } else { '' }
        [void]$sb.AppendLine(('                "{0}"{1}' -f $escaped, $comma))
    }
    [void]$sb.Append('            ])')
    return $sb.ToString()
}

function Update-AppReleaseNotes {
    param(
        [string]$Path,
        [string]$Version,
        [string]$DateValue,
        [string[]]$Items
    )

    $content = Get-Content -Path $Path -Raw -Encoding UTF8
    $block = Format-ReleaseNoteBlock -Version $Version -DateValue $DateValue -Items $Items
    $pattern = '(private static readonly ReleaseNote\[\] Releases =\s*\[\s*)'
    if ($content -notmatch $pattern) {
        throw "Could not locate Releases array in $Path"
    }
    $updated = [regex]::Replace(
        $content,
        $pattern,
        ('$1' + [Environment]::NewLine + $block + ',' + [Environment]::NewLine),
        1
    )
    Set-Content -Path $Path -Value $updated -Encoding UTF8 -NoNewline
}

function Update-ReadmeVersion {
    param(
        [string]$Path,
        [string]$OldVersion,
        [string]$NewVersion
    )

    $content = Get-Content -Path $Path -Raw -Encoding UTF8
    $content = $content.Replace("version-$OldVersion-", "version-$NewVersion-")
    $content = $content.Replace("Windows $OldVersion", "Windows $NewVersion")
    $content = $content.Replace("**$OldVersion**", "**$NewVersion**")
    $content = [regex]::Replace($content, '现为 \*\*[0-9]+\.[0-9]+\.[0-9]+\*\*', "现为 **$NewVersion**")
    Set-Content -Path $Path -Value $content -Encoding UTF8 -NoNewline
}

function Update-InnoVersion {
    param(
        [string]$Path,
        [string]$NewVersion
    )

    $content = Get-Content -Path $Path -Raw -Encoding UTF8
    $updated = [regex]::Replace(
        $content,
        '#define MyAppVersion "[^"]+"',
        ('#define MyAppVersion "{0}"' -f $NewVersion),
        1
    )
    Set-Content -Path $Path -Value $updated -Encoding UTF8 -NoNewline
}

function Reset-UnreleasedFile {
    param([string]$Path)

    $text = @'
# Unreleased

> 自 **上一个已发布版本** 以来的改动先记在这里。
> **默认不升版本号**。只有明确要求「升版本 / bump version」时，才把本文件条目汇总进新版本说明并清空。

## 变更

- （暂无）

## 说明

- 修 bug、加功能、改代码：往「变更」里追加一条，**不要**改 `Quickstart/Quickstart.csproj` 的 `<Version>`。
- 纯文档 / 脚本 / 仓库整理：可记一条，也可不记。
- 升版本时由 `scripts/bump-version.ps1` 读取本文件，写入 `AppReleaseNotes.cs` 并重置本文件。
'@
    Set-Content -Path $Path -Value $text -Encoding UTF8
}

function Write-ReleaseNotesMarkdown {
    param(
        [string]$Path,
        [string]$Version,
        [string[]]$Items
    )

    $dir = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('## 主要更新')
    [void]$sb.AppendLine()
    foreach ($item in $Items) {
        [void]$sb.AppendLine(('- {0}' -f $item))
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## 下载')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine(('- `Quickstart-v{0}-win-x64.exe`：Windows x64 便携版，单文件直接运行。' -f $Version))
    [void]$sb.AppendLine(('- `Quickstart-Setup-v{0}-win-x64.exe`：Windows x64 安装版。' -f $Version))
    [void]$sb.AppendLine('- `SHA256SUMS.txt`：下载文件的 SHA-256 校验值。')

    Set-Content -Path $Path -Value $sb.ToString() -Encoding UTF8
}

$repoRoot = Get-RepoRoot
$projectPath = Join-Path $repoRoot "Quickstart\Quickstart.csproj"
$notesPath = Join-Path $repoRoot "Quickstart\Core\AppReleaseNotes.cs"
$unreleasedPath = Join-Path $repoRoot "docs\UNRELEASED.md"
$readmePath = Join-Path $repoRoot "README.md"
$issPath = Join-Path $repoRoot "installer\Quickstart.iss"

if (-not (Test-Path $projectPath)) { throw "Project not found: $projectPath" }
if (-not (Test-Path $notesPath)) { throw "Release notes not found: $notesPath" }

$oldVersion = Get-ProjectVersion -ProjectPath $projectPath
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-NextPatchVersion -Current $oldVersion
}
if ($Version -eq $oldVersion) {
    throw "New version ($Version) must differ from current version ($oldVersion)."
}
if ([string]::IsNullOrWhiteSpace($Date)) {
    $Date = Get-Date -Format "yyyy-MM-dd"
}

$items = New-Object System.Collections.Generic.List[string]
foreach ($n in @(Get-UnreleasedNotes -Path $unreleasedPath)) {
    if (-not [string]::IsNullOrWhiteSpace($n)) { $items.Add($n.Trim()) | Out-Null }
}
foreach ($n in @($Notes)) {
    if (-not [string]::IsNullOrWhiteSpace($n)) { $items.Add($n.Trim()) | Out-Null }
}

$unique = New-Object System.Collections.Generic.List[string]
$seen = @{}
foreach ($item in $items) {
    $key = $item.ToLowerInvariant()
    if ($seen.ContainsKey($key)) { continue }
    $seen[$key] = $true
    $unique.Add($item) | Out-Null
}
$items = @($unique)

if ($items.Count -eq 0) {
    throw "No release notes found. Add items to docs/UNRELEASED.md or pass -Notes."
}

Write-Host "==> Bump version: $oldVersion -> $Version"
Write-Host "==> Date: $Date"
Write-Host "==> Notes ($($items.Count)):"
$items | ForEach-Object { Write-Host "   - $_" }

if ($WhatIf) {
    Write-Host "WhatIf set; no files modified."
    exit 0
}

Set-ProjectVersion -ProjectPath $projectPath -NewVersion $Version
Update-AppReleaseNotes -Path $notesPath -Version $Version -DateValue $Date -Items $items
if (Test-Path $readmePath) {
    Update-ReadmeVersion -Path $readmePath -OldVersion $oldVersion -NewVersion $Version
}
if (Test-Path $issPath) {
    Update-InnoVersion -Path $issPath -NewVersion $Version
}
Reset-UnreleasedFile -Path $unreleasedPath

$releaseNotesPath = Join-Path $repoRoot "artifacts\release\v$Version\release-notes.md"
Write-ReleaseNotesMarkdown -Path $releaseNotesPath -Version $Version -Items $items

Write-Host ""
Write-Host "Version bump completed: v$Version"
Write-Host "Updated:"
Write-Host "  - Quickstart/Quickstart.csproj"
Write-Host "  - Quickstart/Core/AppReleaseNotes.cs"
Write-Host "  - docs/UNRELEASED.md (reset)"
if (Test-Path $readmePath) { Write-Host "  - README.md" }
if (Test-Path $issPath) { Write-Host "  - installer/Quickstart.iss" }
Write-Host "  - $releaseNotesPath"
Write-Host ""
Write-Host "=============================================="
Write-Host " NEXT: sync to GitHub with release package"
Write-Host "   .\scripts\sync-github.ps1"
Write-Host "   or: sync-github.cmd"
Write-Host " This will commit/push, build Release, and upload"
Write-Host " portable/installer assets to GitHub Releases."
Write-Host "=============================================="
