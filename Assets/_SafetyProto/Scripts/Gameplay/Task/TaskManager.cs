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
    private List<SafetyTask> _remainingFreeTasks = new();
    private HashSet<SafetyTask> _completedTasks = new();
    private HashSet<TaskGroup> _completedGroups = new();

    private IScoreService _scoreService;
    void Start()
    {
        if (!this.IsEventBusReady())
        {
            return;
        }

        if (scoreServiceAsset == null)
        {
            Debug.LogError("ScoreService asset not assigned to TaskManager!", this);
            enabled = false;
            return;
        }

        _scoreService = scoreServiceAsset.Service;

        EventBus.Instance.onActionAttempt.AddListener(HandleActionAttempt);
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
            EventBus.Instance.onActionAttempt.RemoveListener(HandleActionAttempt);
            EventBus.Instance.onTaskTimeout.RemoveListener(HandleTaskTimeout);
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
            bool canStart = group.prerequisites.All(p => _completedGroups.Contains(p));
            if (!canStart)
            {
                Debug.LogWarning($"TaskManager: Skipping group '{group.groupName}' (unmet prerequisites)");
                _currentGroupIndex++;
                continue;
            }

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
            totalTasks: taskGroups.Sum(g => g.tasks.Count)
        );
        Debug.Log("TaskManager: Raising SessionCompleted event with stats: " +
                  $"Time={totalTime}, Score={totalScore}, Completed={completedArgs.tasksCompleted}/{completedArgs.totalTasks}");
        EventBus.Instance.RaiseSessionCompleted(completedArgs);
    }

    public void AbortSessionManually()
    {
        Debug.LogWarning("TaskManager: Session aborted manually.");
        EndSession();
    }

    private void StartNextSequentialTask(TaskGroup group)
    {
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

    private void HandleActionAttempt(ActionAttemptEventArgs args)
    {
        if (_isTaskActive && _currentTask != null)
        {
            if (args.ActionType == _currentTask.expectedAction)
            {
                CompleteCurrentTask();
            }
            return;
        }

        if (_remainingFreeTasks.Count > 0)
        {
            var availableTasks = _remainingFreeTasks.Where(t => t != null).ToList();
            SafetyTask matchedTask = availableTasks.FirstOrDefault(t => t.expectedAction == args.ActionType);
            if (matchedTask != null)
            {
                _remainingFreeTasks.Remove(matchedTask);
                _completedTasks.Add(matchedTask);

                EventBus.Instance.RaiseTaskStarted(new TaskEventArgs(matchedTask));
                EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs(matchedTask));

                if (_remainingFreeTasks.Count == 0)
                {
                    var finishedGroup = taskGroups[_currentGroupIndex];
                    EventBus.Instance.RaiseGroupCompleted(new TaskGroupEventArgs(finishedGroup));
                    _completedGroups.Add(finishedGroup);
                    StartNextGroup();
                }
            }
        }
    }

    private void CompleteCurrentTask()
    {
        EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs(_currentTask));
        _completedTasks.Add(_currentTask);
        _isTaskActive = false;

        if (delayBetweenTasks > 0)
            Invoke(nameof(AdvanceSequential), delayBetweenTasks);
        else
            AdvanceSequential();
    }

    private void AdvanceSequential()
    {
        StartNextSequentialTask(taskGroups[_currentGroupIndex]);
    }

    public void HandleTaskTimeout(TaskEventArgs args)
    {
        if (_isTaskActive && _currentTask != null && args.Task == _currentTask)
        {
            _isTaskActive = false;
            if (delayBetweenTasks > 0)
                Invoke(nameof(AdvanceSequential), delayBetweenTasks);
            else
                AdvanceSequential();
        }
    }

    public SafetyTask GetCurrentTask() => _currentTask;
}