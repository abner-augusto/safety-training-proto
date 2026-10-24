param(
    [string]$OutputDirectory,
    [switch]$BumpVersion,
    [string]$NewVersion,
    [int]$NewVersionCode,
    [switch]$NoCommit,
    [switch]$Development,
    [int]$PollIntervalSeconds = 15,
    [int]$MaxPollAttempts = 240,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Show-Help {
    @'
Build the Android APK for Meta Quest through the connected Unity Editor.

Usage:
  scripts/build-android.ps1 [options]

Options:
  -OutputDirectory <path>     Copy the APK here. Default: $env:SAFETY_ANDROID_OUTPUT_DIR
                              when set, otherwise dist/Builds/Android
  -BumpVersion                Increment the patch version and the Android version code
  -NewVersion <x.y.z>         Version to set before building; only used with -BumpVersion
  -NewVersionCode <int>       Android version code to set; only used with -BumpVersion
  -NoCommit                   With -BumpVersion, leave the bump uncommitted (the APK is
                              then stamped <commit>-dirty). By default the bump is
                              committed on its own before the build.
  -Development                Build a Development APK (SafetyTraining_dev.apk, debuggable, with the
                              session simulator) for scripts/quest-sim-run.ps1. It is not copied to
                              the output directory. Builds without -flto=thin (Android build type
                              Development, restored afterwards): the LTO link alone takes ~20 min.
  -PollIntervalSeconds <int>  Seconds between build status polls (default: 15)
  -MaxPollAttempts <int>      Poll attempts before giving up (default: 240, one hour at
                              the default interval; a release build with LTO takes ~50 min)
  -Help, --help               Show this help and exit

Examples:
  scripts/build-android.ps1
  scripts/build-android.ps1 -BumpVersion
  scripts/build-android.ps1 -BumpVersion -NewVersion 1.2.0 -NewVersionCode 7
  scripts/build-android.ps1 -OutputDirectory D:\Builds

Requires a Unity Editor in the 'ready' state ('unity open .' first).
The APK is copied as SafetyTraining_v<version>.apk.
'@
}

# PowerShell binds --help to the first string parameter instead of -Help, so
# scan the bound values for it.
if ($Help -or $PSBoundParameters.Values -contains '--help') {
    Show-Help
    return
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$previousBuildState = $null
$relativeApkPath = if ($Development) { "Builds/Android/SafetyTraining_dev.apk" } else { "Builds/Android/SafetyTraining.apk" }
$buildOptionArgs = if ($Development) { @("--options", '["Development","AllowDebugging"]') } else { @() }
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

        # The bump must be its own commit before the build: an uncommitted bump stamps the APK
        # "<commit>-dirty" (BuildCommitStamp), which then reads as untracked code in the session logs.
        $settingsPath = "ProjectSettings/ProjectSettings.asset"
        if (-not $NoCommit -and (git status --porcelain -- $settingsPath)) {
            throw "$settingsPath already has uncommitted changes; commit or revert them so the bump commit holds only the version (or pass -NoCommit)."
        }

        Write-Host "  $currentVersion (code $currentCode) -> $NewVersion (code $NewVersionCode)"
        $code = 'UnityEditor.PlayerSettings.bundleVersion = "' + $NewVersion + '"; UnityEditor.PlayerSettings.Android.bundleVersionCode = ' + $NewVersionCode + '; UnityEditor.AssetDatabase.SaveAssets(); return UnityEditor.PlayerSettings.bundleVersion + "|" + UnityEditor.PlayerSettings.Android.bundleVersionCode;'
        Invoke-UnityEval -Code $code | Out-Null

        if (-not $NoCommit) {
            git add -- $settingsPath
            git commit -q -m "build(android): bump version to $NewVersion (code $NewVersionCode)" -- $settingsPath
            if ($LASTEXITCODE -ne 0) { throw "Committing the version bump failed." }
            Write-Host "  committed: $(git log -1 --format='%h %s')"
        }
    }

    $otherChanges = git status --porcelain
    if ($otherChanges) {
        Write-Warning "Uncommitted changes; the APK will be stamped '<commit>-dirty':`n$($otherChanges -join "`n")"
    }

    # A dirty scene makes the build open a modal "save scene?" dialog that blocks the editor
    # until someone clicks it, so the polling below times out. Save first, and only when dirty:
    # rewriting the 2 MB scene can exceed eval's 5 s main-thread limit.
    $saved = Invoke-UnityEval -Code 'var dirty = new System.Collections.Generic.List<string>(); for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++) { var s = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i); if (s.isDirty) dirty.Add(s.name); } if (dirty.Count > 0) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); UnityEditor.AssetDatabase.SaveAssets(); return string.Join(",", dirty);'
    if ($saved) { Write-Host "  saved dirty scenes: $saved" }

    $bundleVersion = Invoke-UnityEval -Code 'return UnityEditor.PlayerSettings.bundleVersion;'

    # Meta's OVRBuildSettingsBuildProcessor re-adds -flto=thin to every Release-type Android build
    # (OVRProjectConfig.enableIL2CPPLTO) and strips it from any other type, so a Development APK
    # switches the Android build type instead of editing the IL2CPP args. The finally block restores
    # the type and the args; androidBuildType is reached by reflection because it is obsolete and
    # eval compiles warnings as errors.
    if ($Development) {
        $previousBuildState = Invoke-UnityEval -Code 'var p = typeof(UnityEditor.EditorUserBuildSettings).GetProperty("androidBuildType"); var before = p.GetValue(null).ToString(); p.SetValue(null, System.Enum.Parse(p.PropertyType, "Development")); return before + "|" + UnityEditor.PlayerSettings.GetAdditionalIl2CppArgs();'
        Write-Host "  Android build type Development for this build (was $($previousBuildState.Split('|')[0])), so IL2CPP skips LTO"
    }
    Write-Host "=== Building Android (version $bundleVersion) ===" -ForegroundColor Cyan

    $dryRun = Invoke-UnityCommand -Arguments (@(
        "command", "build", "--target", "Android",
        "--outputPath", $relativeApkPath, "--dry_run"
    ) + $buildOptionArgs)
    if (-not $dryRun.data.result.valid) {
        $errs = ($dryRun.data.result.validationErrors -join "; ")
        throw "Build validation failed: $errs"
    }

    $build = Invoke-UnityCommand -Arguments (@(
        "command", "build", "--target", "Android",
        "--outputPath", $relativeApkPath, "--confirm"
    ) + $buildOptionArgs)
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
        # Pipeline < 0.6 returned the status as a JSON string; newer releases return an object.
        $inner = $poll.data.result
        if ($inner -is [string]) { $inner = $inner | ConvertFrom-Json }
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
        # The full report is hundreds of KB; surface the first real errors and point at the rest.
        $report = $finalStatus | ConvertTo-Json -Depth 8 -Compress
        $errors = [regex]::Matches($report, '"message":"((?:[^"\]|\.){0,400})') |
            ForEach-Object { $_.Groups[1].Value } |
            Where-Object { $_ -match 'error|FAILURE|failed|Exception' } |
            Select-Object -First 5
        throw "Build finished with result '$($finalStatus.result)' ($($finalStatus.totalErrors) errors). First errors:`n$($errors -join "`n")`nFull report: unity --format json command build_status"
    }

    Write-Host "=== Build succeeded ($($finalStatus.totalErrors) errors, $($finalStatus.totalWarnings) warnings) ===" -ForegroundColor Green

    if (-not (Test-Path $apkPath)) {
        throw "Expected APK not found at $apkPath"
    }

    if ($Development) {
        Write-Host "=== Development APK at $apkPath ===" -ForegroundColor Green
        return
    }

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $destName = "SafetyTraining_v$bundleVersion.apk"
    $destPath = Join-Path $OutputDirectory $destName
    Copy-Item -Path $apkPath -Destination $destPath -Force

    Write-Host "=== Copied to $destPath ===" -ForegroundColor Green
    Get-Item $destPath | Select-Object Name, Length, LastWriteTime
} finally {
    if ($previousBuildState) {
        $buildType, $il2cppArgs = $previousBuildState.Split('|', 2)
        $restore = 'var p = typeof(UnityEditor.EditorUserBuildSettings).GetProperty("androidBuildType"); p.SetValue(null, System.Enum.Parse(p.PropertyType, "' + $buildType + '")); UnityEditor.PlayerSettings.SetAdditionalIl2CppArgs(@"' + $il2cppArgs.Replace('"', '""') + '"); UnityEditor.AssetDatabase.SaveAssets(); return p.GetValue(null) + " | " + UnityEditor.PlayerSettings.GetAdditionalIl2CppArgs();'
        $restored = Invoke-UnityEval -Code $restore
        Write-Host "  restored build type and IL2CPP args: $restored"
    }
    Pop-Location
}
