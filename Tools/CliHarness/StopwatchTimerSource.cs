using System.Diagnostics;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.CliHarness;

public sealed class StopwatchTimerSource : ITimerSource
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    public float ElapsedSeconds => (float)_sw.Elapsed.TotalSeconds;
    public void Reset() => _sw.Restart();
}
