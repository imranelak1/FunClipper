[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $taskRoot
try {
    dotnet publish src/FunClipper.Desktop/FunClipper.Desktop.csproj -c Release --no-self-contained -o artifacts/desktop --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    dotnet publish src/FunClipper.Web/FunClipper.Web.csproj -c Release --no-self-contained -o artifacts/web --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Web build failed.' }
    Write-Output 'Built artifacts/desktop/FunClipper.exe and artifacts/web/FunClipper.Web.exe.'
}
finally { Pop-Location }
