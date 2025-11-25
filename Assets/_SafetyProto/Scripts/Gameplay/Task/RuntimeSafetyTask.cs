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

        public RuntimeSafetyTask(SafetyTask taskData)
        {
            TaskData = taskData;
            State = TaskState.NotStarted;
        }

        public string taskName => TaskData.taskName;
        public ActionType expectedAction => TaskData.expectedAction;
    }
}
