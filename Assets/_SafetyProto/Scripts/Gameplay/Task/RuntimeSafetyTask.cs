/// <summary>
/// A runtime wrapper for a SafetyTask ScriptableObject.
/// This holds the instance-specific state of a task during a session.
/// </summary>
public class RuntimeSafetyTask
{
    /// <summary>A reference to the base task data.</summary>
    public SafetyTask TaskData { get; private set; }

    /// <summary>The current state of this task instance.</summary>
    public TaskState State { get; set; }

    public RuntimeSafetyTask(SafetyTask taskData)
    {
        TaskData = taskData;
        State = TaskState.NotStarted;
    }

    // You can add convenience properties to access underlying data if needed
    public string taskName => TaskData.taskName;
    public ActionType expectedAction => TaskData.expectedAction;
}