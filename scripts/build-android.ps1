param(
    [string]$OutputDirectory,
    [switch]$BumpVersion,
    [string]$NewVersion,
    [int]$NewVersionCode,
    [int]$PollIntervalSeconds = 15,
    [int]$MaxPollAttempts = 80
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$relativeApkPath = "Builds/Android/SafetyTraining.apk"
$apkPath = Join-Path $repoRoot $relativeApkPath

. (Join-Path $PSScriptRoot "lib/Load-DotEnv.ps1")
Import-DotEnv -Path (Join-Path $PSScriptRoot ".env")

if (-not $OutputDirectory) {
    $OutputDirectory = if ($env:SAFETY_ANDROID_OUTPUT_DIR) {
        $env:SAFETY_ANDROID_OUTPUT_DIR
    } else {
        Join-Path $repoRoot "dist/Builds/Android"
    }
}

function Invoke-UnityCommand {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$IgnoreFailure
    )

    $raw = & unity --format json @Arguments 2>&1 | Out-String
    try {
        $parsed = $raw | ConvertFrom-Json
    } catch {
        throw "Failed to parse Unity CLI output for '$($Arguments -join ' ')':`n$raw"
    }

    if (-not $parsed.success -and -not $IgnoreFailure) {
        $msg = ($parsed.errors | ForEach-Object { $_.message }) -join "; "
        throw "Unity CLI command '$($Arguments -join ' ')' failed: $msg"
    }

    return $parsed
}

function Invoke-UnityEval {
    param([Parameter(Mandatory)][string]$Code)
    $result = Invoke-UnityCommand -Arguments @("command", "eval", "--code", $Code)
    return $result.data.result.result
}

Push-Location $repoRoot
try {
    Write-Host "=== Checking Unity Editor ===" -ForegroundColor Cyan
    $status = Invoke-UnityCommand -Arguments @("status")
    if ($status.data.count -lt 1 -or $status.data.instances[0].state -ne "ready") {
        throw "No ready Unity Editor instance found. Run 'unity open .' and wait for state 'ready'."
    }

    if ($BumpVersion) {
        Write-Host "=== Bumping version ===" -ForegroundColor Cyan
        $current = Invoke-UnityEval -Code 'return UnityEditor.PlayerSettings.bundleVersion + "|" + UnityEditor.PlayerSettings.Android.bundleVersionCode;'
        $parts = $current -split '\|'
        $currentVersion = $parts[0]
        $currentCode = [int]$parts[1]

        if (-not $NewVersion) {
            $versionParts = $currentVersion -split '\.'
            $minor = [int]$versionParts[-1] + 1
            $versionParts[-1] = "$minor"
            $NewVersion = ($versionParts -join '.')
        }
        if (-not $NewVersionCode) {
            $NewVersionCode = $currentCode + 1
        }

        Write-Host "  $currentVersion (code $currentCode) -> $NewVersion (code $NewVersionCode)"
        $code = 'UnityEditor.PlayerSettings.bundleVersion = "' + $NewVersion + '"; UnityEditor.PlayerSettings.Android.bundleVersionCode = ' + $NewVersionCode + '; UnityEditor.AssetDatabase.SaveAssets(); return UnityEditor.PlayerSettings.bundleVersion + "|" + UnityEditor.PlayerSettings.Android.bundleVersionCode;'
        Invoke-UnityEval -Code $code | Out-Null
    }

    $bundleVersion = Invoke-UnityEval -Code 'return UnityEditor.PlayerSettings.bundleVersion;'
    Write-Host "=== Building Android (version $bundleVersion) ===" -ForegroundColor Cyan

    $dryRun = Invoke-UnityCommand -Arguments @(
        "command", "build", "--target", "Android",
        "--outputPath", $relativeApkPath, "--dry_run"
    )
    if (-not $dryRun.data.result.valid) {
        $errs = ($dryRun.data.result.validationErrors -join "; ")
        throw "Build validation failed: $errs"
    }

    $build = Invoke-UnityCommand -Arguments @(
        "command", "build", "--target", "Android",
        "--outputPath", $relativeApkPath, "--confirm"
    )
    $buildId = $build.data.result.buildId
    Write-Host "  build queued: $buildId"

    $finalStatus = $null
    for ($i = 0; $i -lt $MaxPollAttempts; $i++) {
        Start-Sleep -Seconds $PollIntervalSeconds
        $poll = Invoke-UnityCommand -Arguments @("command", "build_status") -IgnoreFailure
        if (-not $poll.success) {
            Write-Host "  (transient poll failure, retrying)"
            continue
        }
        $inner = $poll.data.result | ConvertFrom-Json
        if ($inner.status -eq "building") {
            Write-Host "  building... ($($i + 1)/$MaxPollAttempts)"
            continue
        }
        $finalStatus = $inner
        break
    }

    if (-not $finalStatus) {
        throw "Build did not complete within the polling window."
    }
    if ($finalStatus.result -ne "Succeeded") {
        throw "Build finished with result '$($finalStatus.result)': $($finalStatus | ConvertTo-Json -Depth 3)"
    }

    Write-Host "=== Build succeeded ($($finalStatus.totalErrors) errors, $($finalStatus.totalWarnings) warnings) ===" -ForegroundColor Green

    if (-not (Test-Path $apkPath)) {
        throw "Expected APK not found at $apkPath"
    }

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $destName = "SafetyTraining_v$bundleVersion.apk"
    $destPath = Join-Path $OutputDirectory $destName
    Copy-Item -Path $apkPath -Destination $destPath -Force

    Write-Host "=== Copied to $destPath ===" -ForegroundColor Green
    Get-Item $destPath | Select-Object Name, Length, LastWriteTime
} finally {
    Pop-Location
}
