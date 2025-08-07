/// <summary>
/// Defines the possible states for a single SafetyTask during its lifecycle.
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
    CompletedSuccessButUnsafe
}