[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$pidFile = Join-Path $taskRoot 'artifacts\run\web.pid'
if (!(Test-Path -LiteralPath $pidFile)) { Write-Output 'No launcher-owned web service is recorded.'; return }
$webProcessId = 0
if (![int]::TryParse((Get-Content -LiteralPath $pidFile -Raw).Trim(), [ref]$webProcessId) -or $webProcessId -le 0) { throw 'Invalid web service PID file.' }
$processInfo = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $webProcessId"
$expectedExecutable = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts\web\FunClipper.Web.exe'))
if ($processInfo -and [string]::Equals($processInfo.ExecutablePath, $expectedExecutable, [StringComparison]::OrdinalIgnoreCase)) {
    Stop-Process -Id $webProcessId
    Write-Output 'Stopped the launcher-owned web companion.'
}
elseif ($processInfo) { throw 'The recorded PID belongs to a different process. It was left running.' }
Remove-Item -LiteralPath $pidFile
