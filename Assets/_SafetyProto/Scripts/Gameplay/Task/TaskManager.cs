using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.PPE;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.Task
{
    public class TaskManager : MonoBehaviour, ISessionResettable
    {
        [Header("Task Configuration")]
        public List<TaskGroup> taskGroups = new List<TaskGroup>();
        public bool startTasksAutomatically = true;
        public float delayBetweenTasks = 2.0f;

        [Header("Scoring")]
        public ScoreServiceSO scoreServiceAsset;
        public ScoreManagerAdapter scoreManagerAdapter;

        [Header("Validation")]
        public PPEManager ppeManager;

        private readonly List<RuntimeSafetyTask> _sessionTasks = new List<RuntimeSafetyTask>();
        private RuntimeSafetyTask _currentTask;
        private int _currentGroupIndex = -1;
        private int _currentTaskIndex = -1;
        public int CurrentTaskIndex => _currentTaskIndex;

        private IScoreService _scoreService;
        private readonly HashSet<TaskGroup> _completedGroups = new HashSet<TaskGroup>();
        private readonly List<string> _orderViolations = new List<string>();

        private SessionCompletedEventArgs? _lastSessionSummary;
        public SessionCompletedEventArgs? LastSessionSummary => _lastSessionSummary;

        private void Start()
        {
            if (!this.IsEventBusReady()) return;

            _scoreService = scoreServiceAsset?.Service;
            if (_scoreService == null)
            {
                Debug.LogError("TaskManager requires a ScoreService asset.", this);
                enabled = false;
                return;
            }

            if (ppeManager == null)
            {
                ppeManager = FindFirstObjectByType<PPEManager>();
                if (ppeManager == null)
                {
                    Debug.LogWarning("TaskManager: No PPEManager assigned or found. PPE compliance checks will be skipped.");
                }
            }

            InitializeRuntimeTasks();

            EventBus.Instance.onTaskCompleted.AddListener(HandleTaskCompletion);
            EventBus.Instance.onTaskTimeout.AddListener(HandleTaskTimeout);
            EventBus.Instance.onActionAttempt.AddListener(HandleActionAttempt);

            if (startTasksAutomatically)
                StartNextGroup();
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onTaskCompleted.RemoveListener(HandleTaskCompletion);
                EventBus.Instance.onTaskTimeout.RemoveListener(HandleTaskTimeout);
                EventBus.Instance.onActionAttempt.RemoveListener(HandleActionAttempt);
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

            _currentTaskIndex = -1;
        }

        private void HandleActionAttempt(ActionAttemptEventArgs args)
        {
            var currentGroup = GetCurrentGroup();
            if (currentGroup == null)
            {
                return;
            }

            if (currentGroup.executionMode == TaskExecutionMode.Sequential)
            {
                if (_currentTask == null)
                {
                    return;
                }

                if (args.ActionType == _currentTask.expectedAction)
                {
                    ValidateAndCompleteTask(_currentTask);
                }
                else
                {
                    var idxInGroup = currentGroup.tasks.IndexOf(_currentTask.TaskData);
                    bool isFutureTask = currentGroup.tasks.Skip(idxInGroup + 1).Any(t => t.expectedAction == args.ActionType);
                    if (isFutureTask)
                    {
                        _orderViolations.Add($"Out-of-order action {args.ActionType} while current task is {_currentTask.expectedAction}");
                    }
                }
            }
            else // FreeOrder
            {
                var candidate = _sessionTasks
                    .Where(t => t.State == TaskState.NotStarted && currentGroup.tasks.Contains(t.TaskData))
                    .FirstOrDefault(t => t.expectedAction == args.ActionType);

                if (candidate != null)
                {
                    _currentTask = candidate;
                    _currentTaskIndex = _sessionTasks.IndexOf(candidate);
                    ValidateAndCompleteTask(candidate);
                }
            }
        }

        private void ValidateAndCompleteTask(RuntimeSafetyTask task)
        {
            if (task.State == TaskState.CompletedSuccess || task.State == TaskState.CompletedSuccessButUnsafe)
            {
                return;
            }

            bool compliant = ppeManager == null || ppeManager.VerifyPPECompliance(task.TaskData.requiredPPE);
            task.State = compliant ? TaskState.CompletedSuccess : TaskState.CompletedSuccessButUnsafe;
            task.HasMissedPPEOnce = !compliant;
            task.CompletionTime = Time.time;

            TaskEvents.RaiseTaskCompleted(new TaskEventArgs(task.TaskData, task));

            var currentGroup = GetCurrentGroup();
            if (currentGroup != null && currentGroup.executionMode == TaskExecutionMode.FreeOrder)
            {
                CheckGroupCompletion();
            }
        }

        private void HandleTaskCompletion(TaskEventArgs args)
        {
            var runtimeTask = args.RuntimeTask ?? _sessionTasks.FirstOrDefault(t => t.TaskData == args.Task);
            if (runtimeTask == null)
            {
                return;
            }

            if (runtimeTask.State == TaskState.NotStarted)
            {
                runtimeTask.State = TaskState.CompletedSuccess;
            }

            if (_currentTask == runtimeTask)
            {
                _currentTask = null;
                _currentTaskIndex = -1;
            }

            CheckGroupCompletion();

            var currentGroup = GetCurrentGroup();
            if (currentGroup != null && currentGroup.executionMode == TaskExecutionMode.Sequential)
            {
                _ = WaitAndStartNextTask(delayBetweenTasks);
            }
        }

        private void HandleTaskTimeout(TaskEventArgs args)
        {
            var runtimeTask = args.RuntimeTask ?? _sessionTasks.FirstOrDefault(t => t.TaskData == args.Task);
            if (runtimeTask == null)
            {
                return;
            }

            runtimeTask.State = TaskState.CompletedFailure;
            runtimeTask.CompletionTime = Time.time;

            if (_currentTask == runtimeTask)
            {
                _currentTask = null;
                _currentTaskIndex = -1;
            }

            CheckGroupCompletion();

            var currentGroup = GetCurrentGroup();
            if (currentGroup != null && currentGroup.executionMode == TaskExecutionMode.Sequential)
            {
                _ = WaitAndStartNextTask(delayBetweenTasks);
            }
        }

        private void StartNextGroup()
        {
            var nextGroupIndex = _currentGroupIndex + 1;
            while (nextGroupIndex < taskGroups.Count)
            {
                var group = taskGroups[nextGroupIndex];
                bool canStart = group.requiredGroups.All(r => _completedGroups.Contains(r));
                if (canStart)
                {
                    _currentGroupIndex = nextGroupIndex;
                    TaskEvents.RaiseGroupStarted(new TaskGroupEventArgs(group));
                    StartNextTask();
                    return;
                }

                Debug.LogWarning($"Skipping group '{group.groupName}' (unmet dependencies)");
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
                TaskEvents.RaiseTaskStarted(new TaskEventArgs(_currentTask.TaskData, _currentTask));
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
                TaskEvents.RaiseGroupCompleted(new TaskGroupEventArgs(currentGroup));
                _completedGroups.Add(currentGroup);
            }
        }

        private async Awaitable WaitAndStartNextTask(float delay)
        {
            await Awaitable.WaitForSecondsAsync(delay, destroyCancellationToken);
            if (this == null || _currentTask != null)
            {
                return;
            }

            StartNextTask();
        }

        private void EndSession()
        {
            if (_currentTask != null) return;

            Debug.Log("TaskManager: All task groups completed or no groups available.");
            float totalTime = FindFirstObjectByType<TimerSystem>()?.GetTotalSessionTime() ?? 0f;
            int totalScore = _scoreService.CurrentScore;

            var summary = new SessionCompletedEventArgs(
                totalElapsedTime: totalTime,
                totalScore: totalScore,
                tasksCompleted: _sessionTasks.Count(t => t.State == TaskState.CompletedSuccess || t.State == TaskState.CompletedSuccessButUnsafe),
                totalTasks: _sessionTasks.Count,
                orderViolationCount: _orderViolations.Count
            );
            _lastSessionSummary = summary;
            SessionEvents.RaiseSessionCompleted(summary);
        }

        public SafetyTask GetCurrentTaskData() => _currentTask?.TaskData;
        public TaskGroup GetCurrentGroup() =>
            (_currentGroupIndex >= 0 && _currentGroupIndex < taskGroups.Count)
                ? taskGroups[_currentGroupIndex]
                : null;

        public void ResetSession()
        {
            _completedGroups.Clear();
            _orderViolations.Clear();
            _lastSessionSummary = null;
            _currentGroupIndex = -1;
            _currentTaskIndex = -1;
            _currentTask = null;
            InitializeRuntimeTasks();
        }
    }
}
