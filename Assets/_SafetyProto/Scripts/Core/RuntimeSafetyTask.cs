using SafetyProto.Core.Interfaces;

namespace SafetyProto.Core
{
    /// <summary>
    /// A runtime wrapper for a task definition. Holds instance-specific state
    /// (progress, completion time, flags) during a session.
    ///
    /// Engine-independent: holds an <see cref="ISafetyTask"/> reference rather
    /// than the Unity <c>SafetyTask</c> SO, so this type compiles in Core.
    /// </summary>
    public class RuntimeSafetyTask
    {
        public ISafetyTask TaskData { get; }

        public TaskState State { get; set; }

        public bool HasMissedPPEOnce { get; set; }

        public float CompletionTime { get; set; }

        /// <summary>Id of the <c>reportOptions</c> choice the participant picked, for a
        /// hazard-classification task. Empty for every other task and for a plain
        /// confirm/cancel report. Set directly on this instance (rather than threaded
        /// through <c>TaskEventArgs</c>) because the reporter and the task engine share
        /// this same object via <c>TaskManager.GetSessionTasks()</c>.</summary>
        public string ReportedOptionId { get; set; } = string.Empty;

        public bool IsValid { get; private set; } = true;
        public string InvalidReason { get; private set; } = string.Empty;

        public RuntimeSafetyTask(ISafetyTask taskData)
        {
            TaskData = taskData;
            State = TaskState.NotStarted;
        }

        public string id => TaskData?.id ?? string.Empty;
        public string taskName => TaskData?.taskName ?? string.Empty;
        public string ExpectedActionId => TaskData?.ResolveExpectedActionId() ?? string.Empty;

        public void MarkInvalid(string reason)
        {
            IsValid = false;
            InvalidReason = reason;
            if (State == TaskState.NotStarted || State == TaskState.InProgress)
            {
                State = TaskState.NotPerformed;
            }
        }
    }
}
