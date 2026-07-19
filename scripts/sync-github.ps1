param(
    [string]$Message = "",
    [string]$Runtime = "win-x64",
    [switch]$SkipRelease,
    [switch]$SkipInstaller,
    [switch]$SkipBuild,
    [switch]$NoCommit,
    [switch]$AllowDirty
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

function Get-LatestReleaseNotesItems {
    param([string]$NotesPath)

    $text = Get-Content -Path $NotesPath -Raw -Encoding UTF8
    $m = [regex]::Match(
        $text,
        'new\(\s*"(?<ver>[^"]+)"\s*,\s*"(?<date>[^"]+)"\s*,\s*\[(?<body>.*?)\]\s*\)',
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )
    if (-not $m.Success) {
        return @()
    }
    $body = $m.Groups['body'].Value
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($mm in [regex]::Matches($body, '"(.*?)"', [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
        $val = $mm.Groups[1].Value
        $val = $val -replace '\\"', '"' -replace '\\\\', '\'
        if (-not [string]::IsNullOrWhiteSpace($val)) {
            $items.Add($val.Trim()) | Out-Null
        }
    }
    return @($items)
}

function Ensure-ReleaseNotesMarkdown {
    param(
        [string]$Path,
        [string]$Version,
        [string[]]$Items
    )

    if ((Test-Path $Path) -and (Get-Item $Path).Length -gt 0) {
        return
    }

    $dir = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('## 主要更新')
    [void]$sb.AppendLine()
    if ($Items.Count -eq 0) {
        [void]$sb.AppendLine('- 常规更新与修复。')
    } else {
        foreach ($item in $Items) {
            [void]$sb.AppendLine(('- {0}' -f $item))
        }
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
Set-Location $repoRoot

$projectPath = Join-Path $repoRoot "Quickstart\Quickstart.csproj"
$notesCsPath = Join-Path $repoRoot "Quickstart\Core\AppReleaseNotes.cs"
$buildReleaseScript = Join-Path $repoRoot "scripts\build-release.ps1"

if (-not (Test-Path $projectPath)) { throw "Project not found: $projectPath" }
$version = Get-ProjectVersion -ProjectPath $projectPath
$tag = "v$version"

Write-Host "==> Sync GitHub for Quickstart $tag"

$status = git status --porcelain
if (-not $NoCommit) {
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        if ([string]::IsNullOrWhiteSpace($Message)) {
            $Message = "chore: sync $tag"
        }
        Write-Host "==> Committing local changes"
        git add -A
        git commit -m $Message
        if ($LASTEXITCODE -ne 0) {
            throw "git commit failed with exit code $LASTEXITCODE"
        }
    } else {
        Write-Host "==> Working tree clean; skip commit"
    }
} else {
    if (-not [string]::IsNullOrWhiteSpace($status) -and -not $AllowDirty) {
        throw "Working tree is dirty. Commit first, or pass -AllowDirty / omit -NoCommit."
    }
}

Write-Host "==> Pushing branch"
git push origin HEAD
if ($LASTEXITCODE -ne 0) {
    throw "git push failed with exit code $LASTEXITCODE"
}

if ($SkipRelease) {
    Write-Host "SkipRelease set; code pushed only."
    exit 0
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    throw "GitHub CLI (gh) not found in PATH. Install gh and run 'gh auth login'."
}

$items = @(Get-LatestReleaseNotesItems -NotesPath $notesCsPath)
$releaseNotesPath = Join-Path $repoRoot "artifacts\release\v$version\release-notes.md"
Ensure-ReleaseNotesMarkdown -Path $releaseNotesPath -Version $version -Items $items

if (-not $SkipBuild) {
    Write-Host "==> Building release package"
    if ($SkipInstaller) {
        & $buildReleaseScript -Runtime $Runtime -SkipInstaller
    } else {
        & $buildReleaseScript -Runtime $Runtime
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE"
    }
}

$releaseRoot = Join-Path $repoRoot "artifacts\release\v$version\$Runtime"
$portable = Join-Path $releaseRoot "Quickstart-v$version-$Runtime.exe"
$installer = Join-Path $releaseRoot "installer\Quickstart-Setup-v$version-$Runtime.exe"
$hashes = Join-Path $releaseRoot "SHA256SUMS.txt"

if (-not (Test-Path $portable)) {
    throw "Portable EXE not found: $portable  (build first or omit -SkipBuild)"
}

$assets = New-Object System.Collections.Generic.List[string]
$assets.Add($portable) | Out-Null
if (Test-Path $hashes) { $assets.Add($hashes) | Out-Null }
if ((-not $SkipInstaller) -and (Test-Path $installer)) {
    $assets.Add($installer) | Out-Null
}

Write-Host "==> Publishing GitHub Release $tag"
gh release view $tag 1>$null 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Release $tag exists; uploading/replacing assets"
    gh release upload $tag @($assets.ToArray()) --clobber
    if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
    gh release edit $tag --title "Quickstart $tag" --notes-file $releaseNotesPath
    if ($LASTEXITCODE -ne 0) { throw "gh release edit failed" }
} else {
    Write-Host "Creating release $tag"
    gh release create $tag @($assets.ToArray()) --title "Quickstart $tag" --notes-file $releaseNotesPath
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
}

Write-Host ""
Write-Host "Sync completed."
Write-Host "Release: https://github.com/LJ996007/Quickstart/releases/tag/$tag"
Write-Host "Assets:"
$assets | ForEach-Object { Write-Host "  - $_" }
