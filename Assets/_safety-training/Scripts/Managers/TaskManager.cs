using UnityEngine;
using System; // Needed for Action
using System.Collections.Generic; // Needed for List

// Manages the flow of tasks, loads definitions, handles actions
public class TaskManager : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static TaskManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }
    // --- End Singleton Pattern ---

    [Header("Tasks")]
    [SerializeField] public List<TaskDef> taskDefinitions;
    private int currentTaskIndex = -1;
    private TaskDef currentTask;
    public TaskDef CurrentTask => currentTask;
    public int CurrentTaskIndex => currentTaskIndex;
    public int TotalTasks => taskDefinitions != null ? taskDefinitions.Count : 0;

    [Header("Dependencies")]
    [SerializeField] private TimerSystem timerSystem;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private UIManager uiManager; // Reference for basic UI updates

    // Events emitted by TaskManager
    public event Action<TaskDef> OnTaskStarted;
    public event Action<ActionEvent> OnActionAttempt; // Emitted when player attempts *any* action
    public event Action<TaskDef> OnTaskTimeout; // Handled by TimerSystem, TaskManager listens
    public event Action<TaskDef, bool> OnTaskCompleted; // task, success/failure
    public event Action OnAllTasksComplete;

    private void OnEnable()
    {
        // Subscribe to events from other managers
        if (timerSystem != null)
        {
            timerSystem.OnTaskTimeout += HandleTaskTimeout;
        }
        else
        {
            Debug.LogError("TaskManager: TimerSystem reference is missing!");
        }
    }

    private void OnDisable()
    {
        // Unsubscribe from events
        if (timerSystem != null)
        {
            timerSystem.OnTaskTimeout -= HandleTaskTimeout;
        }
    }

    // Called by GameManager to start the sequence
    public void StartTrainingSequence()
    {
        Debug.Log("TaskManager: Starting training sequence...");
        currentTaskIndex = -1; // Reset index
        StartNextTask();
    }

    public void StartNextTask()
    {
        currentTaskIndex++;

        if (taskDefinitions == null || currentTaskIndex >= taskDefinitions.Count)
        {
            // All tasks completed
            Debug.Log("TaskManager: All tasks completed.");
            currentTask = null;
            timerSystem?.StopTimer();
            OnAllTasksComplete?.Invoke();
            return;
        }

        currentTask = taskDefinitions[currentTaskIndex];
        Debug.Log($"TaskManager: Starting Task {currentTaskIndex + 1}/{TotalTasks}: {currentTask.taskName}");

        // Reset and start the timer for the new task
        timerSystem?.ResetTimer();
        timerSystem?.StartTimer(currentTask.timeLimit);

        // Notify listeners that a new task has started
        OnTaskStarted?.Invoke(currentTask);
        uiManager?.UpdateTaskDisplay(currentTask); // Notify UI directly or via event
    }

    // This method is called by the player interaction system when an action occurs
    public void HandleActionAttempt(ActionEvent action)
    {
        if (currentTask == null)
        {
            Debug.Log("TaskManager: Action attempted but no task is active.");
            return;
        }

        Debug.Log($"TaskManager: Player attempted action: {action.type}");

        // Emit the action attempt event (ScoreManager listens to this)
        OnActionAttempt?.Invoke(action);

        // --- Task Completion Logic ---
        // If the attempted action is the *expected* action for the current task,
        // we can consider the action part of the task completed.
        // More complex tasks might require a sequence of actions.
        // For this prototype, let's assume one correct action completes the *action* part.
        // ScoreManager evaluates if it was correct and if PPE was worn.

        // After evaluation by ScoreManager, decide if the task is complete.
        // For a simple "one expected action" task:
        if (action.type == currentTask.expectedAction)
        {
            // Task action part is done. We might stop the timer here, or let it run.
            // Let's stop the timer and consider the task completed successfully *from TaskManager's perspective*.
            // ScoreManager already handled the scoring based on action and PPE.
            CompleteCurrentTask(true); // true means completed successfully before timeout
        }
        // Note: Incorrect actions don't necessarily end the task immediately,
        // they just incur a penalty. The task continues until the correct action is done or it times out.
    }

    // Called by TimerSystem when the time runs out
    private void HandleTaskTimeout()
    {
        if (currentTask == null) return;

        Debug.Log($"TaskManager: Handling timeout for task: {currentTask.taskName}");
        OnTaskTimeout?.Invoke(currentTask); // Notify listeners (ScoreManager)

        // ScoreManager evaluates the timeout penalty

        // Task is completed due to timeout (failure)
        CompleteCurrentTask(false); // false means task failed (timed out)
    }

    private void CompleteCurrentTask(bool success)
    {
        if (currentTask == null) return;

        Debug.Log($"TaskManager: Task '{currentTask.taskName}' completed (Success: {success}).");

        timerSystem?.StopTimer(); // Ensure timer is stopped

        // Notify listeners that the task is completed (whether success or failure)
        OnTaskCompleted?.Invoke(currentTask, success);

        // Move to the next task
        StartNextTask();
    }

    // Method to simulate player actions for testing
    public void SimulateAction(ActionType type, GameObject source = null)
    {
        HandleActionAttempt(new ActionEvent(type, source));
    }

    // Method to manually complete the current task (e.g., dev tool)
    public void ManualCompleteTask(bool success)
    {
        if (currentTask != null)
        {
            if (!success)
            {
                // If manually failing, simulate a timeout evaluation if that's how failures are scored
                scoreManager?.EvaluateTimeout(currentTask);
            }
            else
            {
                // If manually succeeding, ensure score for success + PPE is applied if it wasn't already
                // This is tricky and depends on exact rules. For simplicity, manual complete
                // just moves to the next task after potential timeout penalty if success=false.
                // A better way might be a specific scoring call.
                // Let's just call EvaluateAction as if the correct action happened *now* with current PPE.
                // This might double-score if EvaluateAction was already called.
                // A more robust manual complete would require careful state management.
                // For this prototype, we'll just move on. The scoring should ideally
                // have happened when the player *attempted* the correct action.
            }
            CompleteCurrentTask(success);
        }
        else
        {
            Debug.LogWarning("TaskManager: No current task to manually complete.");
        }
    }
}