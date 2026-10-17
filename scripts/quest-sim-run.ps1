param(
    [ValidateSet("guiado", "avaliacao")]
    [string]$Mode = "guiado",
    [int]$Runs = 1,
    [string]$Script,
    [string]$OutputDirectory,
    [switch]$MetricsCsv,
    [int]$TimeoutSeconds = 300,
    [string]$Serial,
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
  -Help, --help            Show this help and exit

Each run folder gets the simulated session log and perf CSV (written under
files/simulations/ on the device), the app's [SafetyProto] logcat lines, the
per-second VrApi compositor lines and, with -MetricsCsv, the OVR Metrics CSV.

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
            if ($verdict.Line -notmatch "\] Completed") { $failed++ }
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
}

Write-Host "Saved to $OutputDirectory"
if ($failed -gt 0) { exit 1 }
