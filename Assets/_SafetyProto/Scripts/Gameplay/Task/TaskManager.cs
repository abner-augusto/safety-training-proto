using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class TaskManager : MonoBehaviour
{
    [Header("Task Configuration")]
    public List<TaskGroup> taskGroups = new List<TaskGroup>();
    public bool startTasksAutomatically = true;
    public float delayBetweenTasks = 2.0f;

    [Header("Scoring")]
    public ScoreServiceSO scoreServiceAsset;

    // The single source of truth for all task states during the session.
    private readonly List<RuntimeSafetyTask> _sessionTasks = new List<RuntimeSafetyTask>();
    private RuntimeSafetyTask _currentTask;

    private int _currentGroupIndex = -1;
    private IScoreService _scoreService;
    private readonly HashSet<TaskGroup> _completedGroups = new HashSet<TaskGroup>();

    void Start()
    {
        if (!this.IsEventBusReady()) return;

        _scoreService = scoreServiceAsset?.Service;
        if (_scoreService == null)
        {
            Debug.LogError("TaskManager requires a ScoreService asset.", this);
            enabled = false;
            return;
        }

        InitializeRuntimeTasks();

        EventBus.Instance.onTaskCompleted.AddListener(HandleTaskCompletion);
        EventBus.Instance.onTaskTimeout.AddListener(HandleTaskTimeout);
        
        if (startTasksAutomatically)
        {
            StartNextGroup();
        }
    }

    private void OnDestroy()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.onTaskCompleted.RemoveListener(HandleTaskCompletion);
            EventBus.Instance.onTaskTimeout.RemoveListener(HandleTaskTimeout);
        }
    }
    
    private void InitializeRuntimeTasks()
    {
        _sessionTasks.Clear();
        foreach (var group in taskGroups)
        {
            foreach (var taskData in group.tasks)
            {
                _sessionTasks.Add(new RuntimeSafetyTask(taskData));
            }
        }
    }

    private void HandleTaskCompletion(TaskEventArgs args)
    {
        var completedTask = _sessionTasks.FirstOrDefault(t => t.TaskData == args.Task);
        if (completedTask != null && completedTask.State == TaskState.InProgress)
        {
            completedTask.State = TaskState.CompletedSuccess;
            _currentTask = null; // No task is active during the delay
            
            // Check if this completes a group before advancing
            CheckGroupCompletion();
            Invoke(nameof(StartNextTask), delayBetweenTasks);
        }
    }

    public void HandleTaskTimeout(TaskEventArgs args)
    {
        var timedOutTask = _sessionTasks.FirstOrDefault(t => t.TaskData == args.Task);
        if (timedOutTask != null && timedOutTask.State == TaskState.InProgress)
        {
            timedOutTask.State = TaskState.CompletedFailure;
            _currentTask = null;

            CheckGroupCompletion();
            Invoke(nameof(StartNextTask), delayBetweenTasks);
        }
    }

    private void StartNextGroup()
    {
        var nextGroupIndex = _currentGroupIndex + 1;
        
        while (nextGroupIndex < taskGroups.Count)
        {
            var group = taskGroups[nextGroupIndex];
            bool canStart = group.prerequisites.All(p => _completedGroups.Contains(p));
            if (canStart)
            {
                _currentGroupIndex = nextGroupIndex;
                EventBus.Instance.RaiseGroupStarted(new TaskGroupEventArgs(group));
                StartNextTask();
                return;
            }
            
            Debug.LogWarning($"TaskManager: Skipping group '{group.groupName}' (unmet prerequisites)");
            nextGroupIndex++;
        }

        // No more groups to start, end the session.
        EndSession();
    }

    private void StartNextTask()
    {
        TaskGroup currentGroup = GetCurrentGroup();
        if (currentGroup == null)
        {
            EndSession(); // Should be handled by StartNextGroup, but as a safeguard.
            return;
        }

        // Find the next task within the current group that has not been started.
        _currentTask = _sessionTasks.FirstOrDefault(t => 
            t.State == TaskState.NotStarted && currentGroup.tasks.Contains(t.TaskData));

        if (_currentTask != null)
        {
            _currentTask.State = TaskState.InProgress;
            EventBus.Instance.RaiseTaskStarted(new TaskEventArgs(_currentTask.TaskData));
        }
        else
        {
            // No more tasks to start in this group, try to start the next group.
            StartNextGroup();
        }
    }

    private void CheckGroupCompletion()
    {
        TaskGroup currentGroup = GetCurrentGroup();
        if (currentGroup == null || _completedGroups.Contains(currentGroup)) return;

        // Are all tasks belonging to this group now completed?
        bool allTasksDone = _sessionTasks
            .Where(t => currentGroup.tasks.Contains(t.TaskData))
            .All(t => t.State == TaskState.CompletedSuccess || t.State == TaskState.CompletedFailure);

        if (allTasksDone)
        {
            EventBus.Instance.RaiseGroupCompleted(new TaskGroupEventArgs(currentGroup));
            _completedGroups.Add(currentGroup);
        }
    }

    private void EndSession()
    {
        if (_currentTask == null) // Prevent multiple session end calls
        { 
            _currentTask = new RuntimeSafetyTask(ScriptableObject.CreateInstance<SafetyTask>()); // Dummy task to stop further actions
        }
        else
        {
            return;
        }

        Debug.Log("TaskManager: All task groups completed!");
        float totalTime = FindFirstObjectByType<TimerSystem>()?.GetElapsedTime() ?? 0f;
        int totalScore = _scoreService.CurrentScore;
        
        var completedArgs = new SessionCompletedEventArgs(
            totalElapsedTime: totalTime,
            totalScore: totalScore,
            tasksCompleted: _sessionTasks.Count(t => t.State == TaskState.CompletedSuccess),
            totalTasks: _sessionTasks.Count
        );
        
        EventBus.Instance.RaiseSessionCompleted(completedArgs);
    }
    
    public SafetyTask GetCurrentTaskData() => _currentTask?.TaskData;
    private TaskGroup GetCurrentGroup() => (_currentGroupIndex >= 0 && _currentGroupIndex < taskGroups.Count) ? taskGroups[_currentGroupIndex] : null;
}