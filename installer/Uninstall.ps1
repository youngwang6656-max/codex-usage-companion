[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\CodexUsageCompanion'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'CodexUsageCompanion'
$shutdownEventName = 'Local\CodexUsageCompanion.Shutdown'

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

Remove-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

Write-Host 'Codex Usage Companion uninstalled.'
