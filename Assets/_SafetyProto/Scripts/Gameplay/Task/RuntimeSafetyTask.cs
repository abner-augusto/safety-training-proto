using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;

namespace SafetyProto.Gameplay.Task
{
    /// <summary>
    /// A runtime wrapper for a SafetyTask ScriptableObject.
    /// This holds the instance-specific state of a task during a session.
    /// </summary>
    public class RuntimeSafetyTask
    {
        public SafetyTask TaskData { get; }

        public TaskState State { get; set; }

        public bool HasFailedOnce { get; set; }

        public bool HasMissedPPEOnce { get; set; }

        public float CompletionTime { get; set; }

        public bool IsValid { get; private set; } = true;
        public string InvalidReason { get; private set; }

        public RuntimeSafetyTask(SafetyTask taskData)
        {
            TaskData = taskData;
            State = TaskState.NotStarted;
        }

        public string taskName => TaskData.taskName;
        public ActionTypeSO ExpectedAction => TaskData.expectedAction;
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
