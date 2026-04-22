param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Project = "Quickstart/Quickstart.csproj",
    [string]$InstallerScript = "installer/Quickstart.iss",
    [string]$PublishDir = "",
    [string]$OutputDir = "",
    [string]$AppVersion = "",
    [string]$OutputBaseFilename = "",
    [string]$IsccPath = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

function Resolve-RepoPath {
    param(
        [string]$RepoRoot,
        [string]$PathValue
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return ""
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return Join-Path $RepoRoot $PathValue
}

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$csproj = Get-Content -Path $ProjectPath
    $propertyGroups = @($csproj.Project.PropertyGroup)

    $version = @($propertyGroups | Where-Object { $_.Version } | Select-Object -ExpandProperty Version -First 1)[0]
    if (-not [string]::IsNullOrWhiteSpace($version)) {
        return $version.Trim()
    }

    $versionPrefix = @($propertyGroups | Where-Object { $_.VersionPrefix } | Select-Object -ExpandProperty VersionPrefix -First 1)[0]
    if (-not [string]::IsNullOrWhiteSpace($versionPrefix)) {
        return $versionPrefix.Trim()
    }

    return "1.0.0"
}

function Find-Iscc {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath) -and (Test-Path $RequestedPath)) {
        return $RequestedPath
    }

    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = (Resolve-Path (Resolve-RepoPath -RepoRoot $repoRoot -PathValue $Project)).Path
$installerPath = (Resolve-Path (Resolve-RepoPath -RepoRoot $repoRoot -PathValue $InstallerScript)).Path

if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    $AppVersion = Get-ProjectVersion -ProjectPath $projectPath
}

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $repoRoot "Quickstart\bin\$Configuration\net10.0-windows\$Runtime\publish"
} else {
    $PublishDir = Resolve-RepoPath -RepoRoot $repoRoot -PathValue $PublishDir
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\installer"
} else {
    $OutputDir = Resolve-RepoPath -RepoRoot $repoRoot -PathValue $OutputDir
}

if ([string]::IsNullOrWhiteSpace($OutputBaseFilename)) {
    $OutputBaseFilename = "Quickstart-Setup-v$AppVersion-$Runtime"
}

if (-not $SkipPublish) {
    Write-Host "==> Publishing self-contained single-file build for $Runtime"
    dotnet publish $projectPath -c $Configuration -r $Runtime -o $PublishDir
}

if (-not (Test-Path $PublishDir)) {
    throw "Publish directory not found: $PublishDir"
}

$publishDirPath = (Resolve-Path $PublishDir).Path
$exePath = Join-Path $publishDirPath "Quickstart.exe"
if (-not (Test-Path $exePath)) {
    throw "Published EXE not found: $exePath"
}

$isccExe = Find-Iscc -RequestedPath $IsccPath
if ([string]::IsNullOrWhiteSpace($isccExe)) {
    throw "ISCC.exe was not found. Install Inno Setup 6 and run this script again."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outputDirPath = (Resolve-Path $OutputDir).Path

Write-Host "==> Building installer"
$isccArgs = @(
    "/DMyAppVersion=$AppVersion",
    "/DMyPublishDir=$publishDirPath",
    "/DMyOutputDir=$outputDirPath",
    "/DMyOutputBaseFilename=$OutputBaseFilename",
    $installerPath
)

& $isccExe @isccArgs

if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$installerPathFinal = Join-Path $outputDirPath "$OutputBaseFilename.exe"
if (-not (Test-Path $installerPathFinal)) {
    throw "Installer was not created: $installerPathFinal"
}

Write-Host ""
Write-Host "Installer output directory: $outputDirPath"
Write-Host "Installer file name: $(Split-Path -Leaf $installerPathFinal)"
