param(
    [string]$Runtime = "win-x64",
    [string]$Project = "Quickstart/Quickstart.csproj"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = (Resolve-Path (Join-Path $repoRoot $Project)).Path
$debugRoot = Join-Path $repoRoot "artifacts\debug\$Runtime"
$regularBuildRoot = Join-Path $repoRoot "artifacts\debug\build"

if (Test-Path $regularBuildRoot) {
    Remove-Item $regularBuildRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $debugRoot | Out-Null

Write-Host "==> Building Debug version"
Write-Host "==> Output directory: $debugRoot"
dotnet build $projectPath -c Debug -r $Runtime -o $debugRoot

if ($LASTEXITCODE -ne 0) {
    throw "Debug build failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $debugRoot "Quickstart.exe"
if (-not (Test-Path $exePath)) {
    throw "Debug EXE not found: $exePath"
}

Write-Host ""
Write-Host "Debug build completed."
Write-Host "Debug EXE: $exePath"
