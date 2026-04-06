using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.Actions;
using SafetyProto.Utils;
using UnityEngine;
using SafetyProto.Core.Logging;

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

        private readonly List<RuntimeSafetyTask> _sessionTasks = new List<RuntimeSafetyTask>();
        private RuntimeSafetyTask _currentTask;
        private int _currentGroupIndex = -1;
        private int _currentTaskIndex = -1;
        public int CurrentTaskIndex => _currentTaskIndex;
        public RuntimeSafetyTask CurrentRuntimeTask => _currentTask;

        private IScoreService _scoreService;
        private readonly HashSet<TaskGroup> _completedGroups = new HashSet<TaskGroup>();
        private readonly List<string> _orderViolations = new List<string>();

        private SessionCompletedEventArgs? _lastSessionSummary;
        public SessionCompletedEventArgs? LastSessionSummary => _lastSessionSummary;

        private CancellationTokenSource _taskDelayCts;

        private void Start()
        {
            if (!this.IsEventBusReady()) return;

            // Both TaskManager and ScoreManagerAdapter resolve IScoreService independently
            // from scoreServiceAsset.Service — no execution-order dependency between them.
            _scoreService = scoreServiceAsset?.Service;
            if (_scoreService == null)
            {
                SafetyLog.Error("TaskManager requires a ScoreService asset.", this);
                SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                {
                    Source = nameof(TaskManager),
                    Message = "ScoreService asset missing",
                    Details = $"TaskManager '{name}' requires a ScoreServiceSO reference."
                });
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
            _taskDelayCts?.Cancel();
            _taskDelayCts?.Dispose();
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onTaskCompleted.RemoveListener(HandleTaskCompletion);
                EventBus.Instance.onTaskTimeout.RemoveListener(HandleTaskTimeout);
            }
        }

        private void InitializeRuntimeTasks()
        {
            _sessionTasks.Clear();
            if (taskGroups == null)
            {
                _currentTaskIndex = -1;
                return;
            }

            foreach (var group in taskGroups)
            {
                if (group == null || group.tasks == null)
                {
                    continue;
                }

                foreach (var taskData in group.tasks)
                {
                    if (taskData == null)
                    {
                        continue;
                    }

                    var runtimeTask = new RuntimeSafetyTask(taskData);
                    ValidateRuntimeTask(runtimeTask);
                    _sessionTasks.Add(runtimeTask);
                }
            }

            _currentTaskIndex = -1;
        }

        private void ValidateRuntimeTask(RuntimeSafetyTask runtimeTask)
        {
            if (runtimeTask == null || runtimeTask.TaskData == null)
            {
                return;
            }

            var actionId = runtimeTask.ExpectedActionId;
            if (string.IsNullOrEmpty(actionId))
            {
                runtimeTask.MarkInvalid("Expected action id missing.");
                ReportInvalidTask(runtimeTask.TaskData, "Expected action id missing.");
                return;
            }

            if (!ActionResolver.TryResolve(actionId, out _))
            {
                runtimeTask.MarkInvalid($"Action '{actionId}' not found.");
                ReportInvalidTask(runtimeTask.TaskData, $"Action '{actionId}' not found in registry.");
            }
        }

        private void ReportInvalidTask(SafetyTask task, string reason)
        {
            var taskName = task != null ? task.taskName : "<null>";
            var message = $"Task '{taskName}' invalid: {reason}";
            SafetyLog.Error($"[TaskManager] {message}", this);

            SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
            {
                Source = nameof(TaskManager),
                Message = "Invalid task configuration",
                Details = message
            });
        }

        private void HandleTaskCompletion(TaskEventArgs args)
        {
            var runtimeTask = GetRuntimeTask(args);
            if (runtimeTask == null)
            {
                return;
            }

            if (args.RuntimeTask != null)
            {
                runtimeTask.State = args.RuntimeTask.State;
                runtimeTask.CompletionTime = args.RuntimeTask.CompletionTime;
                runtimeTask.HasMissedPPEOnce = args.RuntimeTask.HasMissedPPEOnce;
            }
            else
            {
                if (runtimeTask.State == TaskState.NotStarted ||
                    runtimeTask.State == TaskState.InProgress)
                {
                    runtimeTask.State = TaskState.CompletedSuccess;
                }

                runtimeTask.CompletionTime = Time.time;
            }

            if (_currentTask == runtimeTask)
            {
                _currentTask = null;
                _currentTaskIndex = -1;
            }

            CheckGroupCompletion();

            var currentGroup = GetCurrentGroup();
            if (currentGroup != null)
            {
                _ = WaitAndStartNextTask(delayBetweenTasks);
            }
        }

        private void HandleTaskTimeout(TaskEventArgs args)
        {
            var runtimeTask = GetRuntimeTask(args);
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
            if (currentGroup != null)
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
                bool canStart = true;
                if (group.requiredGroups != null)
                {
                    foreach (var req in group.requiredGroups)
                    {
                        if (!_completedGroups.Contains(req))
                        {
                            canStart = false;
                            break;
                        }
                    }
                }
                if (canStart)
                {
                    _currentGroupIndex = nextGroupIndex;
                    TaskEvents.RaiseGroupStarted(new TaskGroupEventArgs(group));
                    StartNextTask();
                    return;
                }

                SafetyLog.Warning($"Skipping group '{group.groupName}' (unmet dependencies)", this);
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
                (t.State == TaskState.NotStarted || t.State == TaskState.InProgress) &&
                currentGroup.tasks.Contains(t.TaskData));

            if (nextIndex >= 0)
            {
                _currentTaskIndex = nextIndex;
                _currentTask = _sessionTasks[nextIndex];
                if (_currentTask.State == TaskState.NotStarted)
                {
                    _currentTask.State = TaskState.InProgress;
                    TaskEvents.RaiseTaskStarted(new TaskEventArgs(_currentTask.TaskData, _currentTask));
                }
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

            bool allDone = true;
            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                var t = _sessionTasks[i];
                if (!currentGroup.tasks.Contains(t.TaskData)) continue;

                var s = t.State;
                if (s != TaskState.CompletedSuccess &&
                    s != TaskState.CompletedFailure &&
                    s != TaskState.CompletedSuccessButUnsafe)
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone)
            {
                TaskEvents.RaiseGroupCompleted(new TaskGroupEventArgs(currentGroup));
                _completedGroups.Add(currentGroup);
            }
        }

        private async Awaitable WaitAndStartNextTask(float delay)
        {
            _taskDelayCts?.Cancel();
            _taskDelayCts?.Dispose();
            _taskDelayCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

            try
            {
                await Awaitable.WaitForSecondsAsync(delay, _taskDelayCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Only advance if no task is currently running.
            // _currentTask is nulled out in HandleTaskCompletion/HandleTaskTimeout before this await completes.
            if (_currentTask != null) return;
            StartNextTask();
        }

        private void EndSession()
        {
            if (_currentTask != null) return;

            SafetyLog.Info("TaskManager: All task groups completed or no groups available.", this);
            float totalTime = FindFirstObjectByType<TimerSystem>()?.GetTotalSessionTime() ?? 0f;
            int totalScore = _scoreService.CurrentScore;

            int tasksCompletedCount = 0;
            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                var s = _sessionTasks[i].State;
                if (s == TaskState.CompletedSuccess || s == TaskState.CompletedSuccessButUnsafe)
                    tasksCompletedCount++;
            }

            var summary = new SessionCompletedEventArgs(
                totalElapsedTime: totalTime,
                totalScore: totalScore,
                tasksCompleted: tasksCompletedCount,
                totalTasks: _sessionTasks.Count,
                orderViolationCount: _orderViolations.Count
            );
            _lastSessionSummary = summary;
            SessionEvents.RaiseSessionCompleted(summary);
        }

        public IReadOnlyList<RuntimeSafetyTask> GetSessionTasks() => _sessionTasks.AsReadOnly();

        public SafetyTask GetCurrentTaskData() => _currentTask?.TaskData;
        public TaskGroup GetCurrentGroup() =>
            (_currentGroupIndex >= 0 && _currentGroupIndex < taskGroups.Count)
                ? taskGroups[_currentGroupIndex]
                : null;

        public RuntimeSafetyTask FindPendingTaskByActionId(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return null;
            }

            var normalized = actionId.Trim();
            var currentGroup = GetCurrentGroup();
            if (currentGroup == null)
            {
                return null;
            }

            if (currentGroup.executionMode == TaskExecutionMode.Sequential)
            {
                if (MatchesAction(_currentTask, normalized))
                {
                    return _currentTask;
                }

                return null;
            }

            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                var t = _sessionTasks[i];
                var s = t.State;
                if (s != TaskState.NotStarted && s != TaskState.InProgress) continue;
                if (!currentGroup.tasks.Contains(t.TaskData)) continue;
                if (MatchesAction(t, normalized)) return t;
            }
            return null;
        }

        private static bool MatchesAction(RuntimeSafetyTask task, string actionId)
        {
            if (task == null)
            {
                return false;
            }

            var expected = task.ExpectedActionId;
            return !string.IsNullOrEmpty(expected) &&
                   string.Equals(expected, actionId, StringComparison.OrdinalIgnoreCase);
        }

        private RuntimeSafetyTask GetRuntimeTask(TaskEventArgs args)
        {
            var runtimeTask = _sessionTasks.FirstOrDefault(t => t.TaskData == args.Task);
            if (runtimeTask != null)
            {
                return runtimeTask;
            }

            return args.RuntimeTask;
        }

        public void FocusTask(RuntimeSafetyTask runtimeTask)
        {
            if (runtimeTask == null)
            {
                _currentTask = null;
                _currentTaskIndex = -1;
                return;
            }

            _currentTask = runtimeTask;
            _currentTaskIndex = _sessionTasks.IndexOf(runtimeTask);
        }

        public void RegisterOrderViolation(string description)
        {
            if (!string.IsNullOrEmpty(description))
            {
                _orderViolations.Add(description);
            }
        }

        public void ResetSession()
        {
            _taskDelayCts?.Cancel();
            _taskDelayCts?.Dispose();
            _taskDelayCts = null;
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
