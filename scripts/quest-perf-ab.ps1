param(
    [string[]]$Variants = @(),
    [string]$Base = "",
    [string]$Hold = "andaime",
    [int]$HoldSeconds = 120,
    [ValidateSet("guiado", "avaliacao")]
    [string]$Mode = "avaliacao",
    [int]$Repeat = 2,
    [switch]$Build,
    [switch]$Install,
    [string]$OutputDirectory,
    [string]$Serial,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Show-Help {
    @'
Compare launch-extra variants of one Development build on a connected Quest:
each variant runs the same simulator hold, alternating, and the VrApi summaries
are tabulated per variant.

Usage:
  scripts/quest-perf-ab.ps1 -Variants <v1>,<v2>[,...] [options]

Options:
  -Variants <list>         One entry per variant: key=value pairs joined by ';',
                           or 'none' for no extra. Example:
                           -Variants "safety_perf_canvas_ss=1","safety_perf_canvas_ss=0"
  -Base <key=value,...>    Extras every variant gets, e.g.
                           safety_perf_scale=0.8,safety_perf_foveation=0
  -Hold <waypoint>         Waypoint to hold (default: andaime)
  -HoldSeconds <int>       Hold length per run (default: 120)
  -Mode guiado|avaliacao   Session mode (default: avaliacao)
  -Repeat <int>            Rounds; each round runs every variant once, so the
                           variants alternate (default: 2)
  -Build                   First build an LTO Development APK
                           (build-android.ps1 -Development -Lto), then install it
  -Install                 Install Builds/Android/SafetyTraining_dev.apk first
  -OutputDirectory <path>  Default: dist/quest-sim/ab_<timestamp>
  -Serial <serial>         adb device serial when more than one is attached
  -Help, --help            Show this help and exit

Writes one quest-sim-run folder per run and summary.md / summary.csv. Keep the
headset still between runs: the view is wherever it rests.
'@
}

if ($Help -or $PSBoundParameters.Values -contains '--help') {
    Show-Help
    return
}
# pwsh -File hands a comma list over as one string, so split it here.
$Variants = @($Variants | ForEach-Object { $_ -split "," } | Where-Object { $_ })
if ($Variants.Count -lt 1) { throw "Pass -Variants (see -Help)." }

$repoRoot = Split-Path -Parent $PSScriptRoot
$apk = Join-Path $repoRoot "Builds/Android/SafetyTraining_dev.apk"
$adbPrefix = @()
if ($Serial) { $adbPrefix = @("-s", $Serial) }

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ("dist/quest-sim/ab_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

if ($Build) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot "build-android.ps1") -Development -Lto
    if ($LASTEXITCODE -ne 0) { throw "The build failed." }
    $Install = $true
}

if ($Install) {
    # A build can outlast the headset's attention; wait for it rather than fail the install.
    $deadline = (Get-Date).AddMinutes(30)
    while (-not ((& adb @adbPrefix get-state 2>$null) -match "device")) {
        if ((Get-Date) -gt $deadline) { throw "No Quest attached within 30 min." }
        Write-Host "Waiting for the Quest (plug it in and allow USB debugging)..."
        Start-Sleep -Seconds 15
    }
    $installed = (& adb @adbPrefix install -r $apk 2>&1) -join "`n"
    if ($installed -notmatch "Success") { throw "adb install failed: $installed" }
    Write-Host "Installed $apk ($((Get-Item $apk).LastWriteTime))"
}

function Get-Slug([string]$Variant) {
    if ($Variant -eq "none") { return "none" }
    return ($Variant -replace "safety_perf_", "" -replace "[^A-Za-z0-9.=;]", "" -replace "[=;]", "-")
}

$rows = [System.Collections.Generic.List[object]]::new()
for ($round = 1; $round -le $Repeat; $round++) {
    foreach ($variant in $Variants) {
        $pairs = @()
        if ($Base) { $pairs += $Base -split "," }
        if ($variant -ne "none") { $pairs += $variant -split ";" }
        $runDir = Join-Path $OutputDirectory ("{0}_r{1}" -f (Get-Slug $variant), $round)
        $runArgs = @("-NoProfile", "-File", (Join-Path $PSScriptRoot "quest-sim-run.ps1"), "-Mode", $Mode,
            "-Hold", $Hold, "-HoldSeconds", $HoldSeconds, "-OutputDirectory", $runDir)
        if ($pairs) { $runArgs += @("-Extra", (($pairs | Where-Object { $_ }) -join ",")) }
        if ($Serial) { $runArgs += @("-Serial", $Serial) }

        Write-Host "=== round $round, variant '$variant' ===" -ForegroundColor Cyan
        & pwsh @runArgs | Select-String -Pattern "\[run 1\] [\d.,]+ fps" | ForEach-Object { Write-Host $_.Line }

        $summaryPath = Get-ChildItem -Path $runDir -Recurse -Filter vrapi_summary.json -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $summaryPath) {
            Write-Warning "No VrApi summary for round $round, '$variant'; see $runDir."
            continue
        }
        $summary = Get-Content -Raw $summaryPath.FullName | ConvertFrom-Json
        $rows.Add([pscustomobject]@{
            variant = $variant; round = $round; fps = $summary.fpsMean; fpsMin = $summary.fpsMin
            appGpuMs = $summary.appGpuMs; gpuUtil = $summary.gpuUtil; cpuUtil = $summary.cpuUtil
            stale = $summary.staleFrames; seconds = $summary.seconds
        })
    }
}

if ($rows.Count -eq 0) { throw "No run produced a summary." }
$rows | Export-Csv -NoTypeInformation -Encoding utf8 (Join-Path $OutputDirectory "summary.csv")

$mean = { param($items, $name) [Math]::Round(($items | Measure-Object $name -Average).Average, 2) }
$lines = @(
    "# A/B: hold '$Hold', mode $Mode, $Repeat round(s)",
    "",
    "Base extras: ``$(if ($Base) { $Base } else { 'none' })``",
    "",
    "| Variant | Runs | fps | min fps | App GPU ms | GPU util | CPU util | Stale |",
    "|---|---|---|---|---|---|---|---|"
)
foreach ($group in ($rows | Group-Object variant)) {
    $g = $group.Group
    $lines += "| ``$($group.Name)`` | $($g.Count) | $(& $mean $g 'fps') | $(($g | Measure-Object fpsMin -Minimum).Minimum) | $(& $mean $g 'appGpuMs') | $(& $mean $g 'gpuUtil') | $(& $mean $g 'cpuUtil') | $(($g | Measure-Object stale -Sum).Sum) |"
}
$lines += @("", "Per run:", "")
foreach ($row in $rows) {
    $lines += "- round $($row.round) ``$($row.variant)``: $($row.fps) fps, app GPU $($row.appGpuMs) ms, GPU $($row.gpuUtil), $($row.stale) stale over $($row.seconds) s"
}
$lines | Set-Content -Encoding utf8 (Join-Path $OutputDirectory "summary.md")
$lines | ForEach-Object { Write-Host $_ }
Write-Host "Saved to $OutputDirectory"
