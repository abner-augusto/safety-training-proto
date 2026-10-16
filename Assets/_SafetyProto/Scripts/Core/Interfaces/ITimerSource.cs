namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Provides the session clock: seconds of session time as the participant experiences
    /// it, so paused time is excluded. The Unity implementation accumulates unpaused frame
    /// time (<c>TimerSystem</c> over <c>SessionClock</c>); the harness uses a
    /// <c>System.Diagnostics.Stopwatch</c> because it never pauses.
    ///
    /// Nullable in consumers: if no clock is injected, treat elapsed as 0f.
    /// </summary>
    public interface ITimerSource
    {
        /// <summary>Seconds since the session started, excluding paused time.</summary>
        float ElapsedSeconds { get; }
    }
}
