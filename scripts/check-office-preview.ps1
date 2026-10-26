param([switch]$Interactive, [switch]$Portable)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repo 'artifacts/debug/checks/office-preview'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
if (-not (Test-Path (Join-Path $testRoot 'fixture-tools/node_modules/xlsx'))) {
    npm.cmd install --prefix (Join-Path $testRoot 'fixture-tools') xlsx@0.18.5 --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Could not install fixture generator.' }
}
node (Join-Path $PSScriptRoot 'create-office-preview-fixtures.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Could not create fixtures.' }
$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier><SelfContained>true</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../../../../scripts/check-office-preview.cs" />
    <ProjectReference Include="../../../../../Quickstart/Quickstart.csproj" />
  </ItemGroup>
</Project>
'@
$runner = Join-Path $testRoot 'runner'
New-Item -ItemType Directory -Force -Path $runner | Out-Null
[IO.File]::WriteAllText((Join-Path $runner 'PreviewChecks.csproj'), $project)
if ($Portable) {
    $portableDir = Join-Path $testRoot 'portable-runner'
    dotnet publish (Join-Path $runner 'PreviewChecks.csproj') -c Debug -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $portableDir
    if ($LASTEXITCODE -ne 0) { throw 'Portable test runner publish failed.' }
    $runArgs = @($testRoot)
    if ($Interactive) { $runArgs += '--interactive' }
    & (Join-Path $portableDir 'PreviewChecks.exe') @runArgs
} else {
    $runArgs = @('run', '--project', (Join-Path $runner 'PreviewChecks.csproj'), '-c', 'Debug', '--', $testRoot)
    if ($Interactive) { $runArgs += '--interactive' }
    & dotnet @runArgs
}
exit $LASTEXITCODE
