namespace SafetyProto.Core
{
    /// <summary>
    /// Defines the possible states for a single SafetyTask during its lifecycle.
    /// Primary definition in Core (previously in SafetyProto.Data.Enums).
    /// </summary>
    public enum TaskState
    {
        /// <summary>The task has not yet been started.</summary>
        NotStarted,
        /// <summary>The task is currently active and awaiting user action.</summary>
        InProgress,
        /// <summary>The task was completed successfully.</summary>
        CompletedSuccess,
        /// <summary>The task was failed (e.g., by timeout).</summary>
        CompletedFailure,
        /// <summary>The task was completed, but without the correct PPE.</summary>
        CompletedSuccessButUnsafe,
        /// <summary>The task was never attempted and the session moved past it
        /// (Evaluation mode: the participant advanced/finished with this task open).
        /// Terminal, earns nothing; distinct from CompletedFailure (timeout).</summary>
        Omitted
    }
}
