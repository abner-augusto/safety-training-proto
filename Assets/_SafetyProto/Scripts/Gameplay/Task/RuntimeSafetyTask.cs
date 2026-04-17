using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;

namespace SafetyProto.Gameplay.Task
{
    /// <summary>
    /// A runtime wrapper for a task definition. Holds instance-specific state
    /// (progress, completion time, flags) during a session.
    ///
    /// Engine-independent: holds an <see cref="ISafetyTask"/> reference rather
    /// than the Unity <c>SafetyTask</c> SO, so this type compiles in Shared.
    /// </summary>
    public class RuntimeSafetyTask
    {
        public ISafetyTask TaskData { get; }

        public TaskState State { get; set; }

        public bool HasFailedOnce { get; set; }

        public bool HasMissedPPEOnce { get; set; }

        public float CompletionTime { get; set; }

        public bool IsValid { get; private set; } = true;
        public string InvalidReason { get; private set; } = string.Empty;

        public RuntimeSafetyTask(ISafetyTask taskData)
        {
            TaskData = taskData;
            State = TaskState.NotStarted;
        }

        public string taskName => TaskData?.taskName ?? string.Empty;
        public string ExpectedActionId => TaskData?.ResolveExpectedActionId() ?? string.Empty;

        public void MarkInvalid(string reason)
        {
            IsValid = false;
            InvalidReason = reason;
            if (State == TaskState.NotStarted || State == TaskState.InProgress)
            {
                State = TaskState.CompletedFailure;
            }
        }
    }
}
