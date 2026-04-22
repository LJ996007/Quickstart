param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Project = "Quickstart/Quickstart.csproj",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

function Resolve-RepoPath {
    param(
        [string]$RepoRoot,
        [string]$PathValue
    )

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

function Get-Sha256Hex {
    param([string]$FilePath)

    $stream = [System.IO.File]::OpenRead($FilePath)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hashBytes = $sha256.ComputeHash($stream)
        } finally {
            $sha256.Dispose()
        }
    } finally {
        $stream.Dispose()
    }

    return ([System.BitConverter]::ToString($hashBytes)).Replace("-", "")
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = (Resolve-Path (Resolve-RepoPath -RepoRoot $repoRoot -PathValue $Project)).Path
$buildInstallerScript = (Resolve-Path (Join-Path $repoRoot "scripts\build-installer.ps1")).Path

$version = Get-ProjectVersion -ProjectPath $projectPath
$releaseRoot = Join-Path $repoRoot "artifacts\releases\v$version\$Runtime"
$publishDir = Join-Path $releaseRoot "publish"
$installerDir = Join-Path $releaseRoot "installer"
$symbolsDir = Join-Path $releaseRoot "symbols"
$portableAliasName = "Quickstart-v$version-$Runtime.exe"
$portableAliasPath = Join-Path $releaseRoot $portableAliasName
$installerBaseName = "Quickstart-Setup-v$version-$Runtime"
$installerPath = Join-Path $installerDir "$installerBaseName.exe"
$releaseInfoPath = Join-Path $releaseRoot "release-info.txt"
$hashesPath = Join-Path $releaseRoot "SHA256SUMS.txt"

New-Item -ItemType Directory -Force -Path $releaseRoot, $publishDir, $installerDir, $symbolsDir | Out-Null

Write-Host "==> Release version: $version"
Write-Host "==> Release directory: $releaseRoot"
Write-Host "==> Publishing portable build"
dotnet publish $projectPath -c $Configuration -r $Runtime -o $publishDir

$publishExe = Join-Path $publishDir "Quickstart.exe"
if (-not (Test-Path $publishExe)) {
    throw "Published EXE not found: $publishExe"
}

Copy-Item $publishExe $portableAliasPath -Force

$publishPdb = Join-Path $publishDir "Quickstart.pdb"
if (Test-Path $publishPdb) {
    Copy-Item $publishPdb (Join-Path $symbolsDir "Quickstart-v$version-$Runtime.pdb") -Force
}

if (-not $SkipInstaller) {
    & $buildInstallerScript `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -Project $projectPath `
        -PublishDir $publishDir `
        -OutputDir $installerDir `
        -AppVersion $version `
        -OutputBaseFilename $installerBaseName `
        -SkipPublish
}

$hashLines = New-Object System.Collections.Generic.List[string]

$portableHash = Get-Sha256Hex -FilePath $portableAliasPath
$hashLines.Add(("{0} *{1}" -f $portableHash, $portableAliasName)) | Out-Null

if (Test-Path $installerPath) {
    $installerHash = Get-Sha256Hex -FilePath $installerPath
    $hashLines.Add(("{0} *installer\{1}" -f $installerHash, (Split-Path -Leaf $installerPath))) | Out-Null
}

Set-Content -Path $hashesPath -Value $hashLines -Encoding Ascii

$releaseInfo = @(
    "AppName: Quickstart"
    "Version: $version"
    "Runtime: $Runtime"
    "Configuration: $Configuration"
    "GeneratedAtUtc: $([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    "ReleaseRoot: $releaseRoot"
    "PublishDir: $publishDir"
    "PortableExe: $portableAliasPath"
    "InstallerExe: $(if (Test-Path $installerPath) { $installerPath } else { 'NOT_BUILT' })"
    "SymbolsDir: $symbolsDir"
    "HashesFile: $hashesPath"
)

Set-Content -Path $releaseInfoPath -Value $releaseInfo -Encoding Ascii

Write-Host ""
Write-Host "Release completed."
Write-Host "Portable EXE: $portableAliasPath"
Write-Host "Installer EXE: $(if (Test-Path $installerPath) { $installerPath } else { 'NOT_BUILT' })"
Write-Host "Release info: $releaseInfoPath"
