[CmdletBinding()]
param(
    [string]$ArtifactPath,
    [string]$OutputDirectory,
    [ValidateRange(128, 4096)][int]$MaxHeapMB = 512,
    [ValidateRange(1000, 300000)][int]$AnalysisTimeoutMS = 60000
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $OutputDirectory = Join-Path $repoRoot "research\medal\runs\$runName"
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Choose an absent output directory to preserve earlier Evidence: $outputRoot"
}
if (-not (Get-Command node -ErrorAction SilentlyContinue) -or -not (Get-Command npx.cmd -ErrorAction SilentlyContinue)) {
    throw 'Node.js and npm/npx must already be installed. This script does not install prerequisites.'
}
if ($ArtifactPath) {
    $analysisPath = (Resolve-Path -LiteralPath $ArtifactPath).Path
    $artifactFormat = 'auto'
} else {
    $artifactFormat = 'directory'
}
New-Item -ItemType Directory -Path $outputRoot | Out-Null

if (-not $ArtifactPath) {
    # Download a bounded public sample. This is the home page, not the complete web application.
    $htmlFile = Join-Path $outputRoot 'medal-home.html'
    Invoke-WebRequest -Uri 'https://medal.tv/' -OutFile $htmlFile -TimeoutSec 30
    $html = Get-Content -LiteralPath $htmlFile -Raw
    $paths = @([regex]::Matches($html, '<script[^>]+src=["'']([^"'']+)["'']') |
        ForEach-Object { $_.Groups[1].Value } |
        Where-Object { $_ -like '/_next/static/chunks/*.js' } |
        Select-Object -Unique | Select-Object -Last 5)
    if ($paths.Count -eq 0) { throw 'No public Next.js bundle paths were found. Inspect the saved HTML before adapting the capture.' }
    $analysisPath = Join-Path $outputRoot 'public-web-bundles'
    New-Item -ItemType Directory -Path $analysisPath | Out-Null
    $downloadRecords = foreach ($path in $paths) {
        $uri = 'https://medal.tv' + $path
        $head = Invoke-WebRequest -Uri $uri -Method Head -TimeoutSec 30
        if (-not $head.Headers['Content-Length']) {
            throw "Unbounded response: Content-Length absent for $uri"
        }
        if ([long]$head.Headers['Content-Length'] -gt 2000000) {
            throw "Bundle exceeds the 2 MB sample bound: $uri"
        }
        $filePath = Join-Path $analysisPath ([IO.Path]::GetFileName($path))
        Invoke-WebRequest -Uri $uri -OutFile $filePath -TimeoutSec 30
        $file = Get-Item -LiteralPath $filePath
        if ($file.Length -gt 2000000) { throw "Downloaded bundle exceeded its declared bound: $uri" }
        [pscustomobject]@{
            url = $uri
            relative_path = $file.Name
            bytes = $file.Length
            sha256 = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
            retrieved_utc = [DateTime]::UtcNow.ToString('o')
        }
    }
    $downloadRecords | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'download-manifest.json') -Encoding UTF8
}

$fullFile = Join-Path $outputRoot 'rea-application-full.json'
$summaryFile = Join-Path $outputRoot 'rea-summary.json'
$viewInput = Join-Path $outputRoot 'rea-view-input.json'
$reaArgs = @('-y', 'rea-agents@6.4.0', 'analyze-javascript-application', $analysisPath,
    '--artifact-format', $artifactFormat, '--max-heap-mb', "$MaxHeapMB",
    '--analysis-timeout-ms', "$AnalysisTimeoutMS", '--format', 'json', '--full-output')
$commandRecord = [ordered]@{
    rea_version = '6.4.0'
    artifact_path = $analysisPath
    executed_target = $false
    argv = @('npx.cmd') + $reaArgs
    started_utc = [DateTime]::UtcNow.ToString('o')
    sample_scope = $(if ($ArtifactPath) { 'caller-supplied artifact; inert static analysis' } else { 'five public homepage scripts; incomplete web application' })
}
$commandRecord | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputRoot 'command.json') -Encoding UTF8
& npx.cmd @reaArgs | Set-Content -LiteralPath $fullFile -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw "REA analysis failed; inspect the saved output at $fullFile" }

# Reuse complete inline Evidence in a new CLI process. IDs are not portable retained-session handles.
# Node parses the large JSON more economically than Windows PowerShell ConvertFrom-Json.
$projectScript = @'
const fs = require('node:fs');
const [full, request] = process.argv.slice(1);
const envelope = JSON.parse(fs.readFileSync(full, 'utf8').replace(/^\uFEFF/, ''));
if (!envelope.ok || !envelope.data?.evidence_id) {
    console.error('REA did not return application Evidence. Inspect the full output.');
    process.exit(1);
}
fs.writeFileSync(request, JSON.stringify({
    source: {kind: 'inline', evidence: envelope.data}, view: {kind: 'summary'}
}));
'@
& node -e $projectScript $fullFile $viewInput
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the summary view from complete Evidence.' }
& npx.cmd -y rea-agents@6.4.0 inspect-analysis-view $viewInput --format json --full-output |
    Set-Content -LiteralPath $summaryFile -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw "REA summary failed; inspect $summaryFile" }
$summary = Get-Content -LiteralPath $summaryFile -Raw | ConvertFrom-Json
if (-not $summary.ok) { throw "REA summary returned an error; inspect $summaryFile" }
# This is one exact temporary file in the selected output directory, not a recursive cleanup.
Remove-Item -LiteralPath $viewInput
$identity = $summary.data.normalized_result
Write-Output "Evidence saved: $fullFile"
Write-Output "Summary saved: $summaryFile"
Write-Output "Parent Evidence: $($identity.parent_evidence_id)"
Write-Output "Coverage: $($identity.summary.coverage.application.status); semantic $($identity.summary.coverage.semantic.status)"
Write-Output 'Read coverage and unknowns before making implementation claims.'
