namespace SafetyProto.Core
{
    /// <summary>
    /// Risk class of a safety task. The single driver of the scoring economy:
    /// points earned, unsafe-completion earning factor, timeout/gate/distractor
    /// penalties and the medal cap all derive from this tier via
    /// <c>ScoringConfig</c>. Mirrors the eliminatory-fault model used in
    /// Brazilian NR training (critical violations cap the medal).
    /// </summary>
    public enum TaskSeverity
    {
        Minor = 0,
        Moderate = 1,
        Critical = 2
    }
}
