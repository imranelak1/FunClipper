[CmdletBinding()]
param([ValidateRange(1024,65535)][int]$Port = 5180)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $taskRoot
try {
    dotnet run --project src/FunClipper.Web/FunClipper.Web.csproj -c Release --urls "http://127.0.0.1:$Port"
}
finally { Pop-Location }
