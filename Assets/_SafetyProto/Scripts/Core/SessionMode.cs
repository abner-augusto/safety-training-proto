namespace SafetyProto.Core
{
    /// <summary>
    /// How the session presents itself to the participant. Guided is the
    /// training default: the task panel lists every step and gates block until
    /// everything is done. Evaluation (modo Avaliação) hides the task list
    /// behind a general objective, lets every group run free-order and allows
    /// finishing with omissions — feedback moves to the end of the session.
    /// This is presentation/flow semantics, NOT the per-group technical
    /// TaskExecutionModeShared: Evaluation overrides Sequential groups to
    /// behave as FreeOrder at runtime without touching the scenario data.
    /// </summary>
    public enum SessionMode
    {
        Guided = 0,
        Evaluation = 1
    }

    /// <summary>
    /// Process-wide session mode. Static (like ScoreService.Instance and
    /// EventContext) so both hosts and the pure-C# cores read one source of
    /// truth. Set before the session starts (menu flow in Unity, --mode flag
    /// in the CLI); reset with the session.
    /// </summary>
    public static class SessionModeState
    {
        public static SessionMode Current { get; set; } = SessionMode.Guided;

        /// <summary>Log/summary token for the current mode (pt-BR, matches the
        /// research vocabulary: "guiado" / "avaliacao").</summary>
        public static string CurrentName =>
            Current == SessionMode.Evaluation ? "avaliacao" : "guiado";

        public static void Reset() => Current = SessionMode.Guided;
    }
}
