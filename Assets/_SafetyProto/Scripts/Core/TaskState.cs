namespace SafetyProto.Core
{
    public enum TaskState
    {
        NotStarted,
        InProgress,
        CompletedSuccess,
        CompletedSuccessButUnsafe,
        /// <summary>Terminal state of a task the participant did not carry out: a gate
        /// closed the group while the task was still open. Working at height gains
        /// nothing from being done faster, so this module does not distinguish "ran out
        /// of time" from "skipped it" — the reason is metadata on the event stream, not
        /// a separate state. Earns no points and subtracts none.</summary>
        NotPerformed
    }
}
