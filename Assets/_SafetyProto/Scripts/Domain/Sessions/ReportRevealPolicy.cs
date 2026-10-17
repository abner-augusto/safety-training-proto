namespace SafetyProto.Domain.Sessions
{
    /// <summary>
    /// When the finish report may appear while a popup could still be on screen. The report
    /// waits for the popup to be gone, but never longer than <see cref="MaxWaitSeconds"/>: a
    /// popup that is never dismissed must not strand the participant without a report.
    /// </summary>
    public static class ReportRevealPolicy
    {
        /// <summary>Longer than any popup fade; only a popup that is not going away reaches it.</summary>
        public const float MaxWaitSeconds = 2f;

        public static bool ShouldReveal(bool popupDisplayed, float waitedSeconds) =>
            !popupDisplayed || waitedSeconds >= MaxWaitSeconds;
    }
}
