[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$desktopExe = Join-Path $taskRoot 'artifacts\desktop\FunClipper.exe'
$webExe = Join-Path $taskRoot 'artifacts\web\FunClipper.Web.exe'
if (!(Test-Path -LiteralPath $desktopExe) -or !(Test-Path -LiteralPath $webExe)) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$runDirectory = Join-Path $taskRoot 'artifacts\run'
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$health = $null
try { $health = Invoke-RestMethod -Uri 'http://127.0.0.1:5180/api/health' -TimeoutSec 2 } catch { }
if ($health -and $health.product -ne 'FunClipper') { throw 'Port 5180 is used by another service. Start the web companion on another port and configure Connection.' }
if (!$health) {
    $priorUrls = $env:ASPNETCORE_URLS
    try {
        $env:ASPNETCORE_URLS = 'http://127.0.0.1:5180'
        $webProcess = Start-Process -FilePath $webExe -WorkingDirectory (Split-Path -Parent $webExe) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runDirectory 'web-output.log') -RedirectStandardError (Join-Path $runDirectory 'web-error.log')
        Set-Content -LiteralPath (Join-Path $runDirectory 'web.pid') -Value $webProcess.Id
    }
    finally { $env:ASPNETCORE_URLS = $priorUrls }
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 200
        try { $health = Invoke-RestMethod -Uri 'http://127.0.0.1:5180/api/health' -TimeoutSec 1; if ($health.product -eq 'FunClipper') { break } } catch { }
        if ($webProcess.HasExited) { break }
    }
    if (!$health -or $health.product -ne 'FunClipper') { throw 'The web companion could not start. See artifacts/run/web-error.log.' }
}
# A person invoking this launcher wants the interactive desktop window.
Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe)
Write-Output 'FunClipper is open. Capture stays off until you choose Start capture. Quit from the tray menu.'
