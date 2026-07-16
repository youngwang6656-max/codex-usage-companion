[CmdletBinding()]
param(
    [string]$SourceDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $projectRoot 'publish\win-x64'
}

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\CodexUsageCompanion'
$executable = Join-Path $installDirectory 'CodexUsageCompanion.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'CodexUsageCompanion'
$shutdownEventName = 'Local\CodexUsageCompanion.Shutdown'

if (-not (Test-Path -LiteralPath (Join-Path $SourceDirectory 'CodexUsageCompanion.exe'))) {
    throw "Published component not found in $SourceDirectory. Run installer\Build.ps1 first."
}

$running = @(Get-Process -Name 'CodexUsageCompanion' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    try {
        $shutdownEvent = [System.Threading.EventWaitHandle]::OpenExisting($shutdownEventName)
        $null = $shutdownEvent.Set()
        $shutdownEvent.Dispose()
    }
    catch {
        foreach ($process in $running) {
            $null = $process.CloseMainWindow()
        }
    }

    $running | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    $remaining = @(Get-Process -Name 'CodexUsageCompanion' -ErrorAction SilentlyContinue)
    if ($remaining.Count -gt 0) {
        $remaining | Stop-Process -Force -ErrorAction SilentlyContinue
        $remaining | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    }
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
$maximumCopyAttempts = 10
for ($attempt = 1; $attempt -le $maximumCopyAttempts; $attempt++) {
    try {
        Copy-Item -Path (Join-Path $SourceDirectory '*') -Destination $installDirectory -Recurse -Force
        break
    }
    catch {
        if ($attempt -eq $maximumCopyAttempts) {
            throw
        }

        Start-Sleep -Milliseconds 500
    }
}
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty `
    -Path $runKey `
    -Name $runName `
    -PropertyType String `
    -Value ('"{0}" --background' -f $executable) `
    -Force | Out-Null

Start-Process -FilePath $executable -ArgumentList '--background' -WindowStyle Hidden

Write-Host 'Codex Usage Companion installed.'
Write-Host "Executable: $executable"
Write-Host 'Startup: current Windows user'
