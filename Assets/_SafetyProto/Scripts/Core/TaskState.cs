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
        /// <summary>The task was completed, but without the correct PPE.</summary>
        CompletedSuccessButUnsafe,
        /// <summary>Terminal state of a task the participant did not carry out: a gate
        /// closed the group while the task was still open. Working at height gains
        /// nothing from being done faster, so this module does not distinguish "ran out
        /// of time" from "skipped it" — the reason is metadata on the event stream, not
        /// a separate state. Earns no points and subtracts none.</summary>
        NotPerformed
    }
}
