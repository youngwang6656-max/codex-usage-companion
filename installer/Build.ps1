[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$project = Join-Path $projectRoot 'src\CodexUsageCompanion\CodexUsageCompanion.csproj'
$output = Join-Path $projectRoot 'publish\win-x64'
$nugetConfig = Join-Path $projectRoot 'NuGet.Config'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:APPDATA = Join-Path $projectRoot '.build-appdata'

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw ".NET 8 SDK not found at $dotnet"
}

& $dotnet restore $project `
    --configfile $nugetConfig `
    --ignore-failed-sources `
    -p:NuGetAudit=false `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

& $dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $output `
    --no-restore `
    -p:NuGetAudit=false `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published to $output"
