param(
    [ValidateSet("guiado", "avaliacao")]
    [string]$Mode = "guiado",
    [int]$Runs = 1,
    [string]$Script,
    [string]$OutputDirectory,
    [switch]$MetricsCsv,
    [int]$TimeoutSeconds = 300,
    [string]$Serial,
    [string[]]$Extra = @(),
    [string]$Hold,
    [int]$HoldSeconds = 120,
    [ValidateSet("standing", "crouched")]
    [string]$HoldPosture = "standing",
    [switch]$Help
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Show-Help {
    @'
Replay the scenario script on a connected Quest, with nobody wearing it, and
collect what each run recorded. Needs a Development build installed: release
builds ignore the launch request.

Usage:
  scripts/quest-sim-run.ps1 [options]

Options:
  -Mode guiado|avaliacao   Session mode (default: guiado)
  -Runs <int>              Back-to-back runs, one app launch each (default: 1)
  -Script <path>           Local variant scenario JSON to push and play instead
                           of the embedded script (see docs/session-simulator.md)
  -OutputDirectory <path>  Where to save the run folders
                           (default: dist/quest-sim/<timestamp>)
  -MetricsCsv              Also record an OVR Metrics Tool CSV per run
  -TimeoutSeconds <int>    Give up on a run after this long (default: 300)
  -Serial <serial>         adb device serial when more than one is attached
  -Extra <key=value,...>   Additional string intent extras for the launch, e.g.
                           safety_perf_foveation=0.66,safety_perf_viewport=1
                           (XrPerformanceBootstrap's render overrides)
  -Hold <waypoint>         Performance hold: equip the PPE steps of the canonical
                           scenario, move to the waypoint (e.g. irregularity,
                           guardrail, andaime) and stay there. Replaces -Script;
                           the run ends without a terminal snapshot by design
  -HoldSeconds <int>       How long to stay at the waypoint (default: 120)
  -HoldPosture standing|crouched  Posture at the waypoint (default: standing)
  -Help, --help            Show this help and exit

Each run folder gets the simulated session log and perf CSV (written under
files/simulations/ on the device), the app's [SafetyProto] logcat lines, the
per-second VrApi compositor lines with their summary (vrapi_summary.json, also
printed) and, with -MetricsCsv, the OVR Metrics CSV. Compare fps only between
LTO builds (see docs/session-simulator.md "Performance runs").

The proximity sensor is overridden and Guardian paused for the duration so the
headset renders on a desk; the head pose is wherever the headset rests, so keep it still between
runs that are compared.
'@
}

if ($Help -or $PSBoundParameters.Values -contains '--help') {
    Show-Help
    return
}

$package = "com.abnersouza.SafetyProto"
$deviceFiles = "/sdcard/Android/data/$package/files"
$metricsDir = "/sdcard/Android/data/com.oculus.ovrmonitormetricsservice/files/CapturedMetrics"
$metricsReceiver = "com.oculus.ovrmonitormetricsservice/.SettingsBroadcastReceiver"
$repoRoot = Split-Path -Parent $PSScriptRoot

$adbPrefix = @()
if ($Serial) { $adbPrefix = @("-s", $Serial) }

# No param block: adb's own flags (-a, -n, -s, -c) would bind to a declared parameter.
function Invoke-Adb {
    $output = & adb @adbPrefix @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "adb $($args -join ' ') failed: $output" }
    return $output
}

function New-HoldScript([string]$Waypoint, [int]$Seconds, [string]$Posture) {
    $scenario = Get-Content -Raw -Encoding utf8 (Join-Path $repoRoot "Assets/_SafetyProto/Resources/Scenarios/default.json") |
        ConvertFrom-Json
    # Keep the opening PPE steps so the session reaches the waypoint in the state a participant would.
    $steps = [System.Collections.Generic.List[object]]::new()
    foreach ($step in $scenario.script) {
        if ($step.kind -ne "ppe") { break }
        $steps.Add($step)
    }
    # The simulator clamps a step's delay to 30 s, so a long hold is a chain of moves to the same place.
    $steps.Add([pscustomobject]@{ kind = "move"; target = $Waypoint; posture = $Posture; delayMs = 2000 })
    for ($left = $Seconds; $left -gt 0; $left -= 30) {
        $steps.Add([pscustomobject]@{ kind = "move"; target = $Waypoint; posture = $Posture; delayMs = [Math]::Min(30, $left) * 1000 })
    }
    $scenario.script = $steps.ToArray()
    $path = Join-Path ([System.IO.Path]::GetTempPath()) "quest_sim_hold_$Waypoint.json"
    $scenario | ConvertTo-Json -Depth 32 | Set-Content -Encoding utf8 $path
    return $path
}

# Averages the per-second VrApi lines; the first 10 s are dropped as load and settle time.
function Get-VrApiSummary([string]$Path) {
    $field = { param($line, $pattern) if ($line -match $pattern) { [double]$Matches[1] } else { [double]::NaN } }
    $rows = @(Get-Content $Path -ErrorAction SilentlyContinue | Where-Object { $_ -match "FPS=\d+/" } | ForEach-Object {
        [pscustomobject]@{
            Fps = & $field $_ "FPS=(\d+)/"
            App = & $field $_ "App=([\d.]+)ms"
            Gpu = & $field $_ "GPU%=([\d.]+)"
            Cpu = & $field $_ "CPU%=([\d.]+)"
            Stale = & $field $_ "Stale=(\d+)"
        }
    })
    if ($rows.Count -gt 15) { $rows = $rows[10..($rows.Count - 1)] }
    if ($rows.Count -eq 0) { return $null }
    $avg = { param($name) [Math]::Round(($rows | Measure-Object $name -Average).Average, 2) }
    return [pscustomobject]@{
        seconds = $rows.Count
        fpsMean = & $avg "Fps"
        fpsMin = ($rows | Measure-Object Fps -Minimum).Minimum
        appGpuMs = & $avg "App"
        gpuUtil = & $avg "Gpu"
        cpuUtil = & $avg "Cpu"
        staleFrames = ($rows | Measure-Object Stale -Sum).Sum
    }
}

function Get-DeviceFiles([string]$Dir) {
    $listing = & adb @adbPrefix shell "ls '$Dir' 2>/dev/null"
    if (-not $listing) { return @() }
    return @($listing | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

$devices = @(& adb devices | Select-String -Pattern "\tdevice$")
if ($devices.Count -eq 0) { throw "No Quest attached. Plug it in and allow USB debugging." }
if ($devices.Count -gt 1 -and -not $Serial) { throw "More than one device attached; pass -Serial." }

$flags = (Invoke-Adb shell dumpsys package $package) -join "`n"
if ($flags -notmatch "versionName=(\S+)") { throw "$package is not installed." }
$version = $Matches[1]
if ($flags -notmatch "pkgFlags=\[[^\]]*DEBUGGABLE") {
    throw "Installed $package $version is not a Development build; it would ignore the simulation request."
}
# The launcher activity changed between Unity's Activity and GameActivity templates; ask the device.
$activity = (Invoke-Adb shell cmd package resolve-activity --brief $package | Select-Object -Last 1).Trim()


if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ("dist/quest-sim/" + (Get-Date -Format "yyyyMMdd_HHmmss"))
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

if ($Hold) {
    if ($Script) { throw "-Hold and -Script are exclusive: -Hold builds its own script." }
    $Script = New-HoldScript $Hold $HoldSeconds $HoldPosture
    # The hold outlives the 45 s terminal wait, so give the run room to finish.
    $TimeoutSeconds = [Math]::Max($TimeoutSeconds, $HoldSeconds + 120)
}

$scriptOnDevice = $null
if ($Script) {
    $scriptOnDevice = "$deviceFiles/sim_script.json"
    Invoke-Adb push $Script $scriptOnDevice | Out-Null
}

Write-Host "=== $Runs simulated run(s), mode $Mode, $package $version ===" -ForegroundColor Cyan
Write-Host "  device clock: $(Invoke-Adb shell date)"

# Off the head and still, the headset raises the Guardian dialog, which takes focus and pauses the app.
$guardianPause = ((Invoke-Adb shell getprop debug.oculus.guardian_pause) -join "").Trim()
Invoke-Adb shell setprop debug.oculus.guardian_pause 1 | Out-Null
Invoke-Adb shell am broadcast -a com.oculus.vrpowermanager.prox_close | Out-Null
Invoke-Adb shell input keyevent KEYCODE_WAKEUP | Out-Null
# The default ring buffer drops the app's startup lines within a couple of minutes of VrApi output.
Invoke-Adb logcat -G 16M | Out-Null
if ($MetricsCsv) {
    Invoke-Adb shell am broadcast -n $metricsReceiver -a com.oculus.ovrmonitormetricsservice.ENABLE_CSV | Out-Null
}

$failed = 0
try {
    for ($run = 1; $run -le $Runs; $run++) {
        $runDir = Join-Path $OutputDirectory ("run{0:D2}_{1}" -f $run, $Mode)
        New-Item -ItemType Directory -Force $runDir | Out-Null

        Invoke-Adb shell am force-stop $package | Out-Null
        $logsBefore = Get-DeviceFiles "$deviceFiles/simulations"
        $perfBefore = Get-DeviceFiles "$deviceFiles/simulations/perf"
        $metricsBefore = Get-DeviceFiles $metricsDir
        Invoke-Adb logcat -c | Out-Null

        $startArgs = @("shell", "am", "start", "-n", $activity, "--es", "safety_sim", $Mode)
        if ($scriptOnDevice) { $startArgs += @("--es", "safety_sim_script", $scriptOnDevice) }
        # pwsh -File hands a comma list over as one string, so split it here too.
        foreach ($pair in ($Extra | ForEach-Object { $_ -split ',' } | Where-Object { $_ })) {
            $key, $value = $pair.Split('=', 2)
            if (-not $value) { throw "-Extra expects key=value, got '$pair'" }
            $startArgs += @("--es", $key, $value)
        }
        $started = (Invoke-Adb @startArgs) -join "`n"
        if ($started -match "Error") { throw "Launch failed: $started" }
        Write-Host "[run $run] launched at $(Invoke-Adb shell date '+%H:%M:%S')"

        $verdict = $null
        $blocked = $null
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while (-not $verdict -and -not $blocked -and (Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 5
            $verdict = & adb @adbPrefix logcat -d -s Unity:* | Select-String -Pattern "\[DeviceSimulationLauncher\] (Completed|Failed|Cancelled)" | Select-Object -Last 1
            # An OS dialog (Quest Link available, update prompts) holds the launch until someone dismisses it.
            $blocked = & adb @adbPrefix logcat -d | Select-String -Pattern "Launch is blocked because" | Select-Object -Last 1
        }

        # Let the background perf write and the session log land before stopping the app.
        Start-Sleep -Seconds 3
        & adb @adbPrefix logcat -d -v time | Select-String -Pattern "\[SafetyProto\]" | ForEach-Object { $_.Line } |
            Set-Content -Encoding utf8 (Join-Path $runDir "safetyproto_logcat.txt")
        & adb @adbPrefix logcat -d -v time -s VrApi | Select-String -Pattern "FPS=" | ForEach-Object { $_.Line } |
            Set-Content -Encoding utf8 (Join-Path $runDir "vrapi.txt")
        Invoke-Adb shell am force-stop $package | Out-Null
        $summary = Get-VrApiSummary (Join-Path $runDir "vrapi.txt")
        if ($summary) {
            $summary | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $runDir "vrapi_summary.json")
            Write-Host ("[run $run] {0} fps mean (min {1}) | app GPU {2} ms | GPU {3:P0} | CPU {4:P0} | {5} stale over {6} s" -f
                $summary.fpsMean, $summary.fpsMin, $summary.appGpuMs, $summary.gpuUtil, $summary.cpuUtil, $summary.staleFrames, $summary.seconds)
        }

        foreach ($pair in @(@("$deviceFiles/simulations", $logsBefore, ".json"),
                            @("$deviceFiles/simulations/perf", $perfBefore, ".csv"),
                            @($metricsDir, $metricsBefore, ".csv"))) {
            $dir, $before, $extension = $pair
            foreach ($file in (Get-DeviceFiles $dir)) {
                if ($before -contains $file -or -not $file.EndsWith($extension)) { continue }
                if ($dir -eq $metricsDir -and -not $file.StartsWith($package)) { continue }
                Invoke-Adb pull "$dir/$file" (Join-Path $runDir $file) | Out-Null
            }
        }

        if ($verdict) {
            Write-Host "[run $run] $($verdict.Line -replace '^.*\[DeviceSimulationLauncher\] ', '')"
            # A hold script stops at a waypoint, so the terminal-snapshot timeout is its normal end.
            $holdEnded = $Hold -and $verdict.Line -match "Espera terminal excedeu"
            if ($verdict.Line -notmatch "\] Completed" -and -not $holdEnded) { $failed++ }
        } elseif ($blocked) {
            Write-Host "[run $run] launch blocked by an OS dialog on the headset ($($blocked.Line -replace '^.*Launch is blocked because: ', '')). Dismiss it with the headset on, then rerun." -ForegroundColor Yellow
            $failed++
            break
        } else {
            Write-Host "[run $run] no result within $TimeoutSeconds s" -ForegroundColor Yellow
            $failed++
        }
    }
} finally {
    if ($MetricsCsv) {
        & adb @adbPrefix shell am broadcast -n $metricsReceiver -a com.oculus.ovrmonitormetricsservice.DISABLE_CSV | Out-Null
    }
    & adb @adbPrefix shell am broadcast -a com.oculus.vrpowermanager.automation_disable | Out-Null
    & adb @adbPrefix shell setprop debug.oculus.guardian_pause $(if ($guardianPause) { $guardianPause } else { "0" }) | Out-Null
    if ($scriptOnDevice) { & adb @adbPrefix shell rm -f $scriptOnDevice | Out-Null }
    if ($Hold -and $Script) { Remove-Item -ErrorAction SilentlyContinue $Script }
}

Write-Host "Saved to $OutputDirectory"
if ($failed -gt 0) { exit 1 }
