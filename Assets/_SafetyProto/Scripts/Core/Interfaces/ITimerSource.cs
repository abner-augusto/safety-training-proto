namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Provides a session-scoped clock. Unity implementation uses
    /// <c>Time.time - sessionStart</c>; harness implementation uses
    /// <c>System.Diagnostics.Stopwatch</c>.
    ///
    /// Nullable in consumers: if no clock is injected, treat elapsed as 0f.
    /// </summary>
    public interface ITimerSource
    {
        /// <summary>Seconds since the session started.</summary>
        float ElapsedSeconds { get; }
    }
}
