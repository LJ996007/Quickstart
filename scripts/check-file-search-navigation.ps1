param([switch]$Interactive)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repo 'artifacts/debug/checks/file-search-navigation'
$runner = Join-Path $testRoot 'runner'
New-Item -ItemType Directory -Force -Path $runner | Out-Null
$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier><SelfContained>true</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../../../../scripts/check-file-search-navigation.cs" />
    <ProjectReference Include="../../../../../Quickstart/Quickstart.csproj" />
  </ItemGroup>
</Project>
'@
$projectPath = Join-Path $runner 'NavigationChecks.csproj'
[IO.File]::WriteAllText($projectPath, $project)
$runArgs = @('run', '--project', $projectPath, '-c', 'Debug', '--')
if ($Interactive) { $runArgs += '--interactive' }
Push-Location -LiteralPath $repo
try {
    & dotnet @runArgs
    $runnerExit = $LASTEXITCODE
}
finally { Pop-Location }
exit $runnerExit
