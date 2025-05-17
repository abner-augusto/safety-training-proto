using UnityEngine;
using System.Collections.Generic;
using System.Linq; // For FirstOrDefault

public class TaskManager : MonoBehaviour
{
    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;

    [Header("Task Configuration")]
    public List<SafetyTask> tasks = new List<SafetyTask>();
    public bool startTasksAutomatically = true;
    public float delayBetweenTasks = 2.0f;

    private int _currentTaskIndex = -1;
    private SafetyTask _currentTask = null;
    private bool _isTaskActive = false;

    void Start()
    {
        if (eventBus == null)
        {
            Debug.LogError("EventBus not assigned to TaskManager!", this);
            enabled = false; // Disable component if EventBus is missing
            return;
        }

        // Subscribe to events
        eventBus.OnActionAttempt.AddListener(HandleActionAttempt);
        // If you want to auto-restart or handle session start:
        // eventBus.OnSessionStarted.AddListener(ResetAndStartTasks);
        eventBus.OnTaskTimeout.AddListener(HandleTaskTimeout);

        if (startTasksAutomatically)
        {
            StartNextTask();
        }
    }

    private void OnDestroy()
    {
        if (eventBus != null)
        {
            eventBus.OnActionAttempt.RemoveListener(HandleActionAttempt);
            // eventBus.OnSessionStarted.RemoveListener(ResetAndStartTasks);
            eventBus.OnTaskTimeout.RemoveListener(HandleTaskTimeout);
        }
    }

    public void ResetAndStartTasks(SessionStartedEventArgs args = new SessionStartedEventArgs()) // Make args optional for direct calls
    {
        _currentTaskIndex = -1;
        _currentTask = null;
        _isTaskActive = false;
        Debug.Log("TaskManager: Resetting and starting tasks.");
        StartNextTask();
    }

    public void StartNextTask()
    {
        if (tasks.Count == 0)
        {
            Debug.LogWarning("TaskManager: No tasks assigned.");
            return;
        }

        _currentTaskIndex++;
        if (_currentTaskIndex < tasks.Count)
        {
            _currentTask = tasks[_currentTaskIndex];
            _isTaskActive = true;
            eventBus.RaiseTaskStarted(new TaskEventArgs(_currentTask));
            Debug.Log($"TaskManager: Started Task '{_currentTask.taskName}'. Expected Action: {_currentTask.expectedAction}");
        }
        else
        {
            Debug.Log("TaskManager: All tasks completed!");
            // Optionally, raise an "AllTasksCompleted" event or loop
            _isTaskActive = false;
            _currentTask = null;
        }
    }

    private void HandleActionAttempt(ActionAttemptEventArgs args)
    {
        if (!_isTaskActive || _currentTask == null)
        {
            // Can log this as an "unexpected action" if desired, or just ignore
            // Debug.Log($"TaskManager: Received action {args.ActionType} but no task is active or current task is null.");
            return;
        }

        Debug.Log($"TaskManager: Handling Action Attempt '{args.ActionType}' for task '{_currentTask.taskName}'. Expected: '{_currentTask.expectedAction}'");

        // The ScoreManager will ultimately decide points based on this event,
        // but TaskManager validates if it's the *correct* action for the *current* task.
        // ScoreManager will then check PPE and timing.

        // This TaskManager primarily cares if the action completes the current task.
        if (args.ActionType == _currentTask.expectedAction)
        {
            CompleteCurrentTask();
        }
        else
        {
            // Action was wrong for the current task. ScoreManager will penalize.
            // TaskManager doesn't need to do much here other than potentially log.
            Debug.Log($"TaskManager: Action '{args.ActionType}' was not the expected '{_currentTask.expectedAction}' for task '{_currentTask.taskName}'.");
        }
    }

    private void CompleteCurrentTask()
    {
        if (!_isTaskActive || _currentTask == null) return;

        Debug.Log($"TaskManager: Task '{_currentTask.taskName}' requirements met (action matched).");
        eventBus.RaiseTaskCompleted(new TaskEventArgs(_currentTask));
        _isTaskActive = false;
        // _currentTask = null; // Keep current task for ScoreManager until next task starts, or ScoreManager subscribes to TaskStarted

        // Start next task after a delay
        if (delayBetweenTasks > 0)
        {
            Invoke(nameof(StartNextTask), delayBetweenTasks);
        }
        else
        {
            StartNextTask();
        }
    }

    // Called by TimerSystem when a task times out
    public void HandleTaskTimeout(TaskEventArgs args)
    {
        if (_isTaskActive && _currentTask != null && args.Task == _currentTask)
        {
            Debug.LogWarning($"TaskManager: Task '{_currentTask.taskName}' timed out!");
            // ScoreManager will handle penalties. TaskManager just moves on.
            _isTaskActive = false;
            // _currentTask = null; // As above

            if (delayBetweenTasks > 0)
            {
                Invoke(nameof(StartNextTask), delayBetweenTasks);
            }
            else
            {
                StartNextTask();
            }
        }
    }

    // Public accessor for the current task, might be needed by ScoreManager
    // Or ScoreManager can just subscribe to TaskStarted event.
    public SafetyTask GetCurrentTask()
    {
        return _currentTask;
    }
}