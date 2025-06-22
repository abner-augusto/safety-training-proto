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

    private int _currentGroupIndex = -1;
    private int _currentTaskIndex = -1;
    private SafetyTask _currentTask;
    private bool _isTaskActive;
    private List<SafetyTask> _remainingFreeTasks = new List<SafetyTask>();
    private readonly HashSet<SafetyTask> _completedTasks = new HashSet<SafetyTask>();
    private readonly HashSet<TaskGroup> _completedGroups = new HashSet<TaskGroup>();

    private IScoreService _scoreService;

    void Start()
    {
        if (!this.IsEventBusReady()) return;

        if (scoreServiceAsset == null)
        {
            Debug.LogError("ScoreService asset not assigned to TaskManager!", this);
            enabled = false;
            return;
        }
        _scoreService = scoreServiceAsset.Service;

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

    /// <summary>
    /// Advances the session state when a task is completed.
    /// This is now the primary way the TaskManager progresses.
    /// </summary>
    private void HandleTaskCompletion(TaskEventArgs args)
    {
        // Mark the task internally as completed.
        _completedTasks.Add(args.Task);

        if (_isTaskActive && _currentTask == args.Task)
        {
            // Sequential mode task was completed.
            _isTaskActive = false;
            Invoke(nameof(AdvanceSequential), delayBetweenTasks);
        }
        else if (_remainingFreeTasks.Contains(args.Task))
        {
            // Free-order mode task was completed.
            _remainingFreeTasks.Remove(args.Task);
            if (_remainingFreeTasks.Count == 0)
            {
                var finishedGroup = GetCurrentGroup();
                if(finishedGroup != null)
                {
                    EventBus.Instance.RaiseGroupCompleted(new TaskGroupEventArgs(finishedGroup));
                    _completedGroups.Add(finishedGroup);
                }
                StartNextGroup();
            }
        }
    }

    public void HandleTaskTimeout(TaskEventArgs args)
    {
        if (_isTaskActive && _currentTask == args.Task)
        {
            _isTaskActive = false;
            // Also mark as complete to prevent re-completion
            _completedTasks.Add(args.Task); 
            Invoke(nameof(AdvanceSequential), delayBetweenTasks);
        }
    }
    
    private void AdvanceSequential()
    {
        StartNextSequentialTask(GetCurrentGroup());
    }

    private void StartNextSequentialTask(TaskGroup group)
    {
        if (group == null) return;
        
        _currentTaskIndex++;
        if (_currentTaskIndex < group.tasks.Count)
        {
            _currentTask = group.tasks[_currentTaskIndex];
            _isTaskActive = true;
            EventBus.Instance.RaiseTaskStarted(new TaskEventArgs(_currentTask));
        }
        else
        {
            EventBus.Instance.RaiseGroupCompleted(new TaskGroupEventArgs(group));
            _completedGroups.Add(group);
            StartNextGroup();
        }
    }

    public TaskGroup GetCurrentGroup()
    {
        if (_currentGroupIndex >= 0 && _currentGroupIndex < taskGroups.Count)
            return taskGroups[_currentGroupIndex];
        return null;
    }

    private void StartNextGroup()
    {
        _currentGroupIndex++;
        while (_currentGroupIndex < taskGroups.Count)
        {
            var group = taskGroups[_currentGroupIndex];
            if (group.prerequisites.All(p => _completedGroups.Contains(p)))
            {
                _currentTaskIndex = -1;
                _currentTask = null;
                _isTaskActive = false;
                _remainingFreeTasks.Clear();

                EventBus.Instance.RaiseGroupStarted(new TaskGroupEventArgs(group));

                if (group.executionMode == TaskExecutionMode.Sequential)
                {
                    StartNextSequentialTask(group);
                }
                else
                {
                    _remainingFreeTasks = new List<SafetyTask>(group.tasks);
                    Debug.Log($"TaskManager: Starting free-order group '{group.groupName}' with {_remainingFreeTasks.Count} tasks.");
                }
                return;
            }
            
            Debug.LogWarning($"TaskManager: Skipping group '{group.groupName}' (unmet prerequisites)");
            _currentGroupIndex++;
        }
        EndSession();
    }

    private void EndSession()
    {
        Debug.Log("TaskManager: All task groups completed!");
        float totalTime = FindFirstObjectByType<TimerSystem>()?.GetElapsedTime() ?? 0f;
        int totalScore = _scoreService?.CurrentScore ?? 0;
        var completedArgs = new SessionCompletedEventArgs(
            totalElapsedTime: totalTime,
            totalScore: totalScore,
            tasksCompleted: _completedTasks.Count,
            totalTasks: taskGroups.SelectMany(g => g.tasks).Count()
        );
        Debug.Log("TaskManager: Raising SessionCompleted event with stats: " +
                  $"Time={totalTime}, Score={totalScore}, Completed={completedArgs.tasksCompleted}/{completedArgs.totalTasks}");
        EventBus.Instance.RaiseSessionCompleted(completedArgs);
    }
    
    public SafetyTask GetCurrentTask() => _currentTask;
}