using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core.Interfaces;

public class TaskManager : MonoBehaviour, ISessionResettable
{
    [Header("Task Configuration")]
    public List<TaskGroup> taskGroups = new List<TaskGroup>();
    public bool startTasksAutomatically = true;
    public float delayBetweenTasks = 2.0f;

    [Header("Scoring")]
    public ScoreServiceSO scoreServiceAsset;
    public ScoreManagerAdapter scoreManagerAdapter; // <--- Add reference to query penalty flags

    private readonly List<RuntimeSafetyTask> _sessionTasks = new List<RuntimeSafetyTask>();
    private RuntimeSafetyTask _currentTask;
    private int _currentGroupIndex = -1;
    private int _currentTaskIndex = -1;
    public int CurrentTaskIndex => _currentTaskIndex;

    private IScoreService _scoreService;
    private readonly HashSet<TaskGroup> _completedGroups = new HashSet<TaskGroup>();

    private SessionCompletedEventArgs? _lastSessionSummary;
    public SessionCompletedEventArgs? LastSessionSummary => _lastSessionSummary;

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
            StartNextGroup();
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
            foreach (var taskData in group.tasks)
                _sessionTasks.Add(new RuntimeSafetyTask(taskData));

        _currentTaskIndex = -1;
    }

    private void HandleTaskCompletion(TaskEventArgs args)
    {
        if (_currentTask != null && args.Task == _currentTask.TaskData)
        {
            _currentTask.State = TaskState.CompletedSuccess;
            _currentTask = null;
            _currentTaskIndex = -1;

            CheckGroupCompletion();
            Invoke(nameof(StartNextTask), delayBetweenTasks);
        }
    }

    private void HandleTaskTimeout(TaskEventArgs args)
    {
        if (_currentTask != null && args.Task == _currentTask.TaskData)
        {
            _currentTask.State = TaskState.CompletedFailure;
            _currentTask = null;
            _currentTaskIndex = -1;

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
            Debug.LogWarning($"Skipping group '{group.groupName}' (unmet prerequisites)");
            nextGroupIndex++;
        }

        EndSession();
    }

    private void StartNextTask()
    {
        var currentGroup = GetCurrentGroup();
        if (currentGroup == null)
        {
            EndSession();
            return;
        }

        int nextIndex = _sessionTasks.FindIndex(t =>
            t.State == TaskState.NotStarted && currentGroup.tasks.Contains(t.TaskData));

        if (nextIndex >= 0)
        {
            _currentTaskIndex = nextIndex;
            _currentTask = _sessionTasks[nextIndex];
            _currentTask.State = TaskState.InProgress;
            EventBus.Instance.RaiseTaskStarted(new TaskEventArgs(_currentTask.TaskData));
        }
        else
        {
            StartNextGroup();
        }
    }

    private void CheckGroupCompletion()
    {
        var currentGroup = GetCurrentGroup();
        if (currentGroup == null || _completedGroups.Contains(currentGroup)) return;

        bool allDone = _sessionTasks
            .Where(t => currentGroup.tasks.Contains(t.TaskData))
            .All(t => t.State == TaskState.CompletedSuccess || t.State == TaskState.CompletedFailure || t.State == TaskState.CompletedSuccessButUnsafe);

        if (allDone)
        {
            EventBus.Instance.RaiseGroupCompleted(new TaskGroupEventArgs(currentGroup));
            _completedGroups.Add(currentGroup);
        }
    }

    private void EndSession()
    {
        if (_currentTask != null) return;

        Debug.Log("TaskManager: All task groups completed!");
        float totalTime = FindFirstObjectByType<TimerSystem>()?.GetTotalSessionTime() ?? 0f;
        int totalScore = _scoreService.CurrentScore;

        var summary = new SessionCompletedEventArgs(
            totalElapsedTime: totalTime,
            totalScore: totalScore,
            tasksCompleted: _sessionTasks.Count(t => t.State == TaskState.CompletedSuccess || t.State == TaskState.CompletedSuccessButUnsafe),
            totalTasks: _sessionTasks.Count
        );
        _lastSessionSummary = summary;
        EventBus.Instance.RaiseSessionCompleted(summary);
    }

    public SafetyTask GetCurrentTaskData() => _currentTask?.TaskData;
    public TaskGroup GetCurrentGroup() =>
        (_currentGroupIndex >= 0 && _currentGroupIndex < taskGroups.Count)
            ? taskGroups[_currentGroupIndex]
            : null;

    public void ResetSession()
    {
        CancelInvoke(nameof(StartNextTask));
        _completedGroups.Clear();
        _lastSessionSummary = null;
        _currentGroupIndex = -1;
        _currentTaskIndex = -1;
        _currentTask = null;
        InitializeRuntimeTasks();
    }
}
