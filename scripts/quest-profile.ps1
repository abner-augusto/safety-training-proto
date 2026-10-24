param(
    [string]$Hold = "irregularity",
    [ValidateSet("guiado", "avaliacao")]
    [string]$Mode = "avaliacao",
    [string[]]$Extra = @(),
    [int]$WarmupSeconds = 40,
    [int]$Frames = 400,
    [string]$Marker,
    [string]$FromCapture,
    [string]$OutputDirectory,
    [string]$Serial,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Show-Help {
    @'
Record a Unity Profiler capture of a Development build on a connected Quest,
held at one waypoint by the session simulator, and rank the CPU cost per
marker. The connected Unity Editor does the recording; nobody wears the headset.

Usage:
  scripts/quest-profile.ps1 [options]

Options:
  -Hold <waypoint>         Waypoint the simulator holds (default: irregularity)
  -Mode guiado|avaliacao   Session mode (default: avaliacao)
  -Extra <key=value,...>   Launch extras passed to quest-sim-run, e.g.
                           safety_perf_scale=0.8,safety_perf_foveation=0
  -WarmupSeconds <int>     Wait after launch before recording (default: 40)
  -Frames <int>            Frames to record and aggregate (default: 400)
  -Marker <text>           Also write the per-frame time of markers whose name
                           contains this text (marker_frames.txt), to see
                           whether a cost is steady or periodic
  -FromCapture <file>      Skip the headset: load a saved capture.data and
                           analyse it again (another -Marker or -Frames)
  -OutputDirectory <path>  Default: dist/quest-sim/profile_<timestamp>
  -Serial <serial>         adb device serial when more than one is attached
  -Help, --help            Show this help and exit

Writes capture.data (open it in the Profiler window with Load), breakdown.txt
(ms per frame by marker path, main and render threads, depth 4) and the
quest-sim-run output under run/. The profiler costs a few fps: rank costs with
it, and measure fps with quest-sim-run alone.
'@
}

if ($Help -or $PSBoundParameters.Values -contains '--help') {
    Show-Help
    return
}

$package = "com.abnersouza.SafetyProto"
$repoRoot = Split-Path -Parent $PSScriptRoot
$adbPrefix = @()
if ($Serial) { $adbPrefix = @("-s", $Serial) }

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ("dist/quest-sim/profile_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

function Invoke-Eval([string]$Code) {
    $json = unity --format json command eval --code $Code | ConvertFrom-Json
    if (-not $json.success -or -not $json.data.result.success) {
        throw "eval failed: $($json | ConvertTo-Json -Depth 8 -Compress)"
    }
    return $json.data.result.result
}

function Invoke-Analysis {
    $breakdownPath = Join-Path $OutputDirectory "breakdown.txt"
    Invoke-Eval @"
int last = UnityEditorInternal.ProfilerDriver.lastFrameIndex; int first = System.Math.Max(UnityEditorInternal.ProfilerDriver.firstFrameIndex, last - $Frames + 1);
var agg = new System.Collections.Generic.Dictionary<string, double[]>();
double frameMs = 0; int n = 0;
for (int f = first; f <= last; f++) {
  for (int t = 0; t < 64; t++) {
    using (var h = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(f, t, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime, false)) {
      if (h == null || !h.valid) break;
      string tn = h.threadName;
      if (t == 0) { frameMs += h.frameTimeMs; n++; }
      if (!(t == 0 || tn.Contains("Render"))) continue;
      var stack = new System.Collections.Generic.List<(int id, int d, string p)> { (h.GetRootItemID(), 0, tn) };
      var kids = new System.Collections.Generic.List<int>();
      while (stack.Count > 0) {
        var top = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
        if (top.d >= 4) continue;
        h.GetItemChildren(top.id, kids);
        foreach (var c in kids.ToArray()) {
          string p = top.p + " > " + h.GetItemName(c);
          if (!agg.TryGetValue(p, out var v)) agg[p] = v = new double[3];
          v[0] += h.GetItemColumnDataAsDouble(c, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime);
          v[1] += h.GetItemColumnDataAsDouble(c, UnityEditor.Profiling.HierarchyFrameDataView.columnSelfTime);
          v[2] += h.GetItemColumnDataAsDouble(c, UnityEditor.Profiling.HierarchyFrameDataView.columnCalls);
          stack.Add((c, top.d + 1, p));
        }
      }
    }
  }
}
var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double[]>>(agg);
list.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));
var sb = new System.Text.StringBuilder();
sb.AppendLine("frames " + first + ".." + last + "  avg frame " + (frameMs / n).ToString("F2") + " ms");
sb.AppendLine("total ms  self ms  calls  (per frame)  marker path");
foreach (var kv in list) {
  if (kv.Value[0] / n < 0.1) continue;
  sb.AppendLine((kv.Value[0] / n).ToString("F2").PadLeft(8) + (kv.Value[1] / n).ToString("F2").PadLeft(9) + (kv.Value[2] / n).ToString("F1").PadLeft(7) + "  " + kv.Key);
}
System.IO.File.WriteAllText(@"$breakdownPath", sb.ToString());
return n;
"@ | Out-Null

    if ($Marker) {
        $markerPath = Join-Path $OutputDirectory "marker_frames.txt"
        $escaped = $Marker.Replace('"', '\"')
        Invoke-Eval @"
int last = UnityEditorInternal.ProfilerDriver.lastFrameIndex; int first = System.Math.Max(UnityEditorInternal.ProfilerDriver.firstFrameIndex, last - $Frames + 1);
var sb = new System.Text.StringBuilder();
for (int f = first; f <= last; f++) {
  using (var h = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(f, 0, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime, false)) {
    double ms = 0;
    var stack = new System.Collections.Generic.List<int> { h.GetRootItemID() };
    var kids = new System.Collections.Generic.List<int>();
    while (stack.Count > 0) {
      int id = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
      h.GetItemChildren(id, kids);
      foreach (var c in kids.ToArray()) {
        if (h.GetItemName(c).Contains("$escaped")) ms += h.GetItemColumnDataAsDouble(c, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime);
        else stack.Add(c);
      }
    }
    sb.AppendLine(f + " " + ms.ToString("F2"));
  }
}
System.IO.File.WriteAllText(@"$markerPath", sb.ToString());
return true;
"@ | Out-Null
        Write-Host "Per-frame '$Marker' time: $markerPath"
    }

    Get-Content $breakdownPath -TotalCount 25
    Write-Host "Breakdown: $breakdownPath"
}

if ($FromCapture) {
    $capture = (Resolve-Path $FromCapture).Path
    Invoke-Eval "UnityEditorInternal.ProfilerDriver.LoadProfile(@`"$capture`", false); return UnityEditorInternal.ProfilerDriver.lastFrameIndex;" | Out-Null
    Write-Host "Loaded $capture; aggregating the last $Frames frames."
    Invoke-Analysis
    return
}

# Hold long enough to cover the warmup, the recording at a low frame rate, and the analysis.
$holdSeconds = $WarmupSeconds + [int][Math]::Ceiling($Frames / 25) + 60
$runArgs = @("-NoProfile", "-File", (Join-Path $PSScriptRoot "quest-sim-run.ps1"), "-Mode", $Mode,
    "-Hold", $Hold, "-HoldSeconds", $holdSeconds, "-OutputDirectory", (Join-Path $OutputDirectory "run"))
if ($Extra) { $runArgs += @("-Extra", ($Extra -join ",")) }
if ($Serial) { $runArgs += @("-Serial", $Serial) }
$runLog = Join-Path $OutputDirectory "run.log"
$run = Start-Process pwsh -ArgumentList $runArgs -NoNewWindow -PassThru -RedirectStandardOutput $runLog

try {
    # The player opens its profiler socket once the app is up.
    $deadline = (Get-Date).AddSeconds(90)
    while (-not (& adb @adbPrefix shell cat /proc/net/unix 2>$null | Select-String "Unity-$package")) {
        if ((Get-Date) -gt $deadline -or $run.HasExited) { throw "The app never opened its profiler socket; see $runLog." }
        Start-Sleep -Seconds 2
    }
    Write-Host "App up; warming up $WarmupSeconds s at '$Hold'."
    Start-Sleep -Seconds $WarmupSeconds

    & adb @adbPrefix forward tcp:34999 "localabstract:Unity-$package" | Out-Null
    # DirectURLConnect silently stays on the Editor; DirectIPConnect reaches the player.
    $connected = Invoke-Eval @'
UnityEditorInternal.ProfilerDriver.profileEditor = false;
UnityEditorInternal.ProfilerDriver.DirectIPConnect("127.0.0.1:34999");
UnityEditorInternal.ProfilerDriver.ClearAllFrames();
UnityEditorInternal.ProfilerDriver.enabled = true;
return UnityEditorInternal.ProfilerDriver.GetConnectionIdentifier(UnityEditorInternal.ProfilerDriver.connectedProfiler);
'@
    if ($connected -notmatch "Player") { throw "Profiler connected to '$connected', not the player." }

    $deadline = (Get-Date).AddSeconds([Math]::Max(60, $Frames / 10))
    do {
        Start-Sleep -Seconds 2
        $last = [int](Invoke-Eval "return UnityEditorInternal.ProfilerDriver.lastFrameIndex;")
        if ((Get-Date) -gt $deadline) { throw "Only $last frames arrived; is the app still running?" }
    } while ($last -lt $Frames + 20)

    $dataPath = Join-Path $OutputDirectory "capture.data"
    Invoke-Eval @"
UnityEditorInternal.ProfilerDriver.enabled = false;
UnityEditorInternal.ProfilerDriver.SaveProfile(@"$dataPath");
return UnityEditorInternal.ProfilerDriver.lastFrameIndex;
"@ | Out-Null
    Write-Host "Saved $dataPath; aggregating the last $Frames frames."

    Invoke-Analysis
} finally {
    & adb @adbPrefix forward --remove tcp:34999 2>$null | Out-Null
    if (-not $run.HasExited) {
        Write-Host "Waiting for the hold run to finish (log: $runLog)."
        $run.WaitForExit()
    }
}
