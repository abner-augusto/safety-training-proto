#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using RuntimeSafetyTask = SafetyProto.Core.RuntimeSafetyTask;

namespace SafetyProto.Domain.Tasks
{
    public sealed class TaskManagerCore : IDisposable
    {
        private readonly IEventBus _bus;
        private readonly IScoreService _scoreService;
        private readonly ITimerSource? _timer;
        private readonly IAsyncScheduler? _scheduler;
        private readonly IHarnessLogger? _logger;
        private readonly IReadOnlyList<ITaskGroup> _taskGroups;
        private readonly float _delayBetweenTasks;

        private readonly List<RuntimeSafetyTask> _sessionTasks = new List<RuntimeSafetyTask>();
        private readonly HashSet<ITaskGroup> _completedGroups = new HashSet<ITaskGroup>();
        private readonly List<string> _orderViolations = new List<string>();

        private RuntimeSafetyTask? _currentTask;
        private int _currentGroupIndex = -1;
        private int _currentTaskIndex = -1;
        private SessionCompletedEventArgs? _lastSessionSummary;

        private readonly Action<TaskEventArgs> _onTaskLifecycle;
        private CancellationTokenSource? _taskDelayCts;

        private bool _subscribed;
        private bool _disposed;

        public int CurrentTaskIndex => _currentTaskIndex;
        public RuntimeSafetyTask? CurrentRuntimeTask => _currentTask;
        public SessionCompletedEventArgs? LastSessionSummary => _lastSessionSummary;

        public TaskManagerCore(
            IEventBus bus,
            IScoreService scoreService,
            IReadOnlyList<ITaskGroup> taskGroups,
            ITimerSource? timer = null,
            IAsyncScheduler? scheduler = null,
            IHarnessLogger? logger = null,
            float delayBetweenTasks = 0f)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _scoreService = scoreService ?? throw new ArgumentNullException(nameof(scoreService));
            _taskGroups = taskGroups ?? throw new ArgumentNullException(nameof(taskGroups));
            _timer = timer;
            _scheduler = scheduler;
            _logger = logger;
            _delayBetweenTasks = delayBetweenTasks;

            _onTaskLifecycle = HandleTaskLifecycle;

            InitializeRuntimeTasks();
        }

        public void Subscribe()
        {
            if (_subscribed) return;
            _bus.Subscribe(_onTaskLifecycle);
            _subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!_subscribed) return;
            _bus.Unsubscribe(_onTaskLifecycle);
            _subscribed = false;
        }

        public void StartSession()
        {
            StartNextGroup();
        }

        private void HandleTaskLifecycle(TaskEventArgs args)
        {
            switch (args.Phase)
            {
                case TaskPhase.Completed: OnTaskCompleted(args); break;
                case TaskPhase.Timeout:   OnTaskTimeout(args); break;
            }
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            var runtimeTask = GetRuntimeTask(args);
            if (runtimeTask == null) return;

            if (args.RuntimeTask != null)
            {
                // External caller provided complete instance — copy state.
                runtimeTask.State = args.RuntimeTask.State;
                runtimeTask.CompletionTime = args.RuntimeTask.CompletionTime;
                runtimeTask.HasMissedPPEOnce = args.RuntimeTask.HasMissedPPEOnce;
            }
            else
            {
                // No external instance — determine state from payload.
                runtimeTask.CompletionTime = _timer?.ElapsedSeconds ?? 0f;
                if (runtimeTask.State == TaskState.NotStarted ||
                    runtimeTask.State == TaskState.InProgress)
                {
                    runtimeTask.State = args.WasPpeCompliant
                        ? TaskState.CompletedSuccess
                        : TaskState.CompletedSuccessButUnsafe;
                }
                runtimeTask.HasMissedPPEOnce = !args.WasPpeCompliant;
            }

            if (ReferenceEquals(_currentTask, runtimeTask))
            {
                _currentTask = null;
                _currentTaskIndex = -1;
            }

            CheckGroupCompletion();

            if (GetCurrentGroup() != null)
            {
                _ = WaitAndStartNextTaskAsync(_delayBetweenTasks);
            }
        }

        private void OnTaskTimeout(TaskEventArgs args)
        {
            var runtimeTask = GetRuntimeTask(args);
            if (runtimeTask == null) return;

            runtimeTask.State = TaskState.CompletedFailure;
            runtimeTask.CompletionTime = _timer?.ElapsedSeconds ?? 0f;

            if (ReferenceEquals(_currentTask, runtimeTask))
            {
                _currentTask = null;
                _currentTaskIndex = -1;
            }

            CheckGroupCompletion();

            if (GetCurrentGroup() != null)
            {
                _ = WaitAndStartNextTaskAsync(_delayBetweenTasks);
            }
        }

        private void InitializeRuntimeTasks()
        {
            _sessionTasks.Clear();
            if (_taskGroups == null) return;

            foreach (var group in _taskGroups)
            {
                if (group == null || group.tasks == null) continue;

                foreach (var taskData in group.tasks)
                {
                    if (taskData == null) continue;
                    _sessionTasks.Add(new RuntimeSafetyTask(taskData));
                }
            }

            _currentTaskIndex = -1;
        }

        private void StartNextGroup()
        {
            var nextGroupIndex = _currentGroupIndex + 1;
            while (nextGroupIndex < _taskGroups.Count)
            {
                var group = _taskGroups[nextGroupIndex];
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
                    _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));
                    StartNextTask();
                    return;
                }

                _logger?.Warning($"TaskManagerCore: Skipping group '{group.groupName}' (unmet dependencies).");
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

            int nextIndex = -1;
            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                var t = _sessionTasks[i];
                if ((t.State == TaskState.NotStarted || t.State == TaskState.InProgress) &&
                    ContainsByReference(currentGroup.tasks, t.TaskData))
                {
                    nextIndex = i;
                    break;
                }
            }

            if (nextIndex >= 0)
            {
                _currentTaskIndex = nextIndex;
                _currentTask = _sessionTasks[nextIndex];
                if (_currentTask.State == TaskState.NotStarted)
                {
                    _currentTask.State = TaskState.InProgress;
                    _bus.Publish(new TaskEventArgs(_currentTask.TaskData, _currentTask, TaskPhase.Started));
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
                if (!ContainsByReference(currentGroup.tasks, t.TaskData)) continue;

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
                _bus.Publish(new TaskGroupEventArgs(currentGroup, TaskGroupPhase.Completed));
                _completedGroups.Add(currentGroup);
            }
        }

        private async System.Threading.Tasks.Task WaitAndStartNextTaskAsync(float delay)
        {
            if (_scheduler == null || delay <= 0f)
            {
                if (_currentTask != null) return;
                StartNextTask();
                return;
            }

            _taskDelayCts?.Cancel();
            _taskDelayCts?.Dispose();
            _taskDelayCts = new CancellationTokenSource();

            try
            {
                await _scheduler.DelayAsync(delay, _taskDelayCts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_currentTask != null) return;
            StartNextTask();
        }

        private void EndSession()
        {
            if (_currentTask != null) return;

            _logger?.Info("TaskManagerCore: All task groups completed or no groups available.");

            float totalTime = _timer?.ElapsedSeconds ?? 0f;
            int totalScore = _scoreService.CurrentScore;

            int tasksCompletedCount = 0;
            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                var s = _sessionTasks[i].State;
                if (s == TaskState.CompletedSuccess || s == TaskState.CompletedSuccessButUnsafe)
                {
                    tasksCompletedCount++;
                }
            }

            var summary = new SessionCompletedEventArgs(
                totalElapsedTime: totalTime,
                totalScore: totalScore,
                tasksCompleted: tasksCompletedCount,
                totalTasks: _sessionTasks.Count,
                orderViolationCount: _orderViolations.Count
            );
            _lastSessionSummary = summary;
            _bus.Publish(summary);
        }

        public IReadOnlyList<RuntimeSafetyTask> GetSessionTasks() => _sessionTasks;

        public ITaskGroup? GetCurrentGroup() =>
            (_currentGroupIndex >= 0 && _currentGroupIndex < _taskGroups.Count)
                ? _taskGroups[_currentGroupIndex]
                : null;

        public RuntimeSafetyTask? FindPendingTaskByActionId(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId)) return null;
            var normalized = actionId.Trim();
            var currentGroup = GetCurrentGroup();
            if (currentGroup == null) return null;

            if (currentGroup.executionMode == TaskExecutionModeShared.Sequential)
            {
                return MatchesAction(_currentTask, normalized) ? _currentTask : null;
            }

            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                var t = _sessionTasks[i];
                var s = t.State;
                if (s != TaskState.NotStarted && s != TaskState.InProgress) continue;
                if (!ContainsByReference(currentGroup.tasks, t.TaskData)) continue;
                if (MatchesAction(t, normalized)) return t;
            }
            return null;
        }

        public void FocusTask(RuntimeSafetyTask? runtimeTask)
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

        private RuntimeSafetyTask? GetRuntimeTask(TaskEventArgs args)
        {
            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                if (ReferenceEquals(_sessionTasks[i].TaskData, args.Task))
                {
                    return _sessionTasks[i];
                }
            }
            return args.RuntimeTask;
        }

        private static bool ContainsByReference(IReadOnlyList<ISafetyTask> tasks, ISafetyTask target)
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                if (ReferenceEquals(tasks[i], target)) return true;
            }
            return false;
        }

        private static bool MatchesAction(RuntimeSafetyTask? task, string actionId)
        {
            if (task == null) return false;
            var expected = task.ExpectedActionId;
            return !string.IsNullOrEmpty(expected) &&
                   string.Equals(expected, actionId, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _taskDelayCts?.Cancel();
            _taskDelayCts?.Dispose();
            Unsubscribe();
            _disposed = true;
        }
    }
}
