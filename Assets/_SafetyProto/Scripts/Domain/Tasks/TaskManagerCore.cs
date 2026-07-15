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

        /// <summary>Evaluation mode overrides every group to free-order semantics;
        /// Guided respects the authored mode. All mode branches in this engine must
        /// go through here — reading group.executionMode directly reintroduces
        /// sequential enforcement in Evaluation.</summary>
        private static TaskExecutionModeShared EffectiveMode(ITaskGroup group) =>
            SessionModeState.Current == SessionMode.Evaluation
                ? TaskExecutionModeShared.FreeOrder
                : group.executionMode;

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

        /// <summary>
        /// Called when the current group's time limit elapses (driven by the Runtime timer —
        /// see <c>TimerSystem.onTimerTimeout</c>). Force-fails any task in the current group
        /// that hasn't reached a terminal state, then replays the exact same
        /// completion/orchestration path a natural last-task completion would take
        /// (<see cref="CheckGroupCompletion"/> → next task/group → <see cref="EndSession"/>).
        /// This is what makes a group timeout drive the session to a terminal state — and, via
        /// <see cref="EndSession"/>, dispatch <c>SessionCompleted</c>/<c>SessionEnded</c> — the
        /// same way normal completion does, instead of leaving the session stuck with a dead
        /// timer and a group that can never finish.
        /// Mirrors <see cref="ForceCompleteAllPendingTasks"/> in that it does not publish a
        /// <c>TaskEventArgs</c> per forced task (no per-task timeout penalty is scored for them),
        /// consistent with how leftover pending tasks are already handled at session end.
        /// Safe to call when there is no current group, or when the current group already
        /// finished (e.g. the last task completed the same frame the timer expired) — both are
        /// no-ops beyond the idempotent orchestration replay.
        /// </summary>
        public void HandleGroupTimeout()
        {
            var currentGroup = GetCurrentGroup();
            if (currentGroup == null) return;

            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                var t = _sessionTasks[i];
                if (!ContainsByReference(currentGroup.tasks, t.TaskData)) continue;

                if (t.State == TaskState.NotStarted || t.State == TaskState.InProgress)
                {
                    t.State = TaskState.CompletedFailure;
                    t.CompletionTime = _timer?.ElapsedSeconds ?? 0f;
                }
            }

            _currentTask = null;
            _currentTaskIndex = -1;

            CheckGroupCompletion();

            if (GetCurrentGroup() != null)
            {
                _ = WaitAndStartNextTaskAsync(_delayBetweenTasks);
            }
        }

        /// <summary>
        /// Evaluation-mode primitive used by both phase gates: closes every pending
        /// task in the CURRENT group as <see cref="TaskState.Omitted"/>, raising one
        /// TASK_OMITTED safety violation per task (0 points — omissions earn nothing
        /// and charge nothing; the foregone points are the cost), then replays the
        /// normal completion orchestration so GroupCompleted / next group / EndSession
        /// fire exactly as a natural completion would. No-op when no group is active.
        /// Returns the omitted tasks (callers drive consequences/UI from them).
        /// </summary>
        public IReadOnlyList<RuntimeSafetyTask> MarkPendingTasksOmitted()
        {
            var omitted = new List<RuntimeSafetyTask>();
            var currentGroup = GetCurrentGroup();
            if (currentGroup == null) return omitted;

            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                var t = _sessionTasks[i];
                if (!ContainsByReference(currentGroup.tasks, t.TaskData)) continue;

                if (t.State == TaskState.NotStarted || t.State == TaskState.InProgress)
                {
                    t.State = TaskState.Omitted;
                    t.CompletionTime = _timer?.ElapsedSeconds ?? 0f;
                    omitted.Add(t);

                    _bus.Publish(new SafetyViolationEventArgs
                    {
                        ViolationCode = "TASK_OMITTED",
                        Message = $"Tarefa omitida pelo participante: {t.taskName}",
                        TaskId = t.id,
                        GroupId = currentGroup.id,
                        TaskName = t.taskName,
                        GroupName = currentGroup.groupName
                    });
                }
            }

            _currentTask = null;
            _currentTaskIndex = -1;

            CheckGroupCompletion();

            if (GetCurrentGroup() != null)
            {
                _ = WaitAndStartNextTaskAsync(_delayBetweenTasks);
            }

            return omitted;
        }

        /// <summary>
        /// Names of tasks in the CURRENT group whose completion order deviated from
        /// the authored task order. Compares CompletionTime timestamps of tasks that
        /// reached a completed state (success or unsafe) against the sequence in
        /// which the group declares them: a task completed EARLIER than a task that
        /// precedes it in the JSON is a deviation. Never-completed tasks are ignored
        /// (omissions are reported separately). Empty list = order respected.
        /// </summary>
        public IReadOnlyList<string> GetCompletionOrderDeviations()
        {
            var deviations = new List<string>();
            var currentGroup = GetCurrentGroup();
            if (currentGroup == null) return deviations;

            float lastTime = float.MinValue;
            for (int i = 0; i < currentGroup.tasks.Count; i++)
            {
                RuntimeSafetyTask? runtime = null;
                for (int j = 0; j < _sessionTasks.Count; j++)
                {
                    if (ReferenceEquals(_sessionTasks[j].TaskData, currentGroup.tasks[i]))
                    {
                        runtime = _sessionTasks[j];
                        break;
                    }
                }

                if (runtime == null) continue;
                var s = runtime.State;
                if (s != TaskState.CompletedSuccess && s != TaskState.CompletedSuccessButUnsafe) continue;

                if (runtime.CompletionTime < lastTime)
                {
                    deviations.Add(runtime.taskName);
                }
                lastTime = runtime.CompletionTime;
            }
            return deviations;
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
                    s != TaskState.CompletedSuccessButUnsafe &&
                    s != TaskState.Omitted)
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

            // Domain-level terminal signal, published unconditionally alongside the summary so
            // any path that reaches EndSession() (normal completion or a group-timeout cascade
            // via HandleGroupTimeout) drives the session to the same terminal state. Previously
            // SessionEnded was only raised by TrainingSessionManager.OnDestroy — a Unity
            // lifecycle hook tied to scene unload/app quit, not to the session actually
            // finishing — which meant a timed-out group never produced SessionEnded at all.
            // Publishing it here also makes it observable from a pure-domain integration test
            // (no Unity Mono layer required).
            _bus.Publish(new SessionEndedEventArgs());
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

            if (EffectiveMode(currentGroup) == TaskExecutionModeShared.Sequential)
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

        /// <summary>
        /// True when <paramref name="type"/> belongs to an equip-set step that comes AFTER the
        /// current step in a Sequential group — i.e. the player is equipping ahead of the
        /// recommended order. Pickup is still allowed; callers use this only to surface an order
        /// hint. Returns false for FreeOrder groups, current/prior-step PPE, and types not owned
        /// by any equip task.
        /// </summary>
        public bool IsPpeAheadOfCurrentStep(PPEType type)
        {
            var group = GetCurrentGroup();
            if (group == null || EffectiveMode(group) != TaskExecutionModeShared.Sequential) return false;
            if (_currentTask == null) return false;

            var tasks = group.tasks;
            int activeIdx = -1, owningIdx = -1;
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                if (ReferenceEquals(t, _currentTask.TaskData)) activeIdx = i;
                // The "owning" step is the first task that introduces this PPE type.
                if (owningIdx < 0 && IsEquipTask(t) && RequiresPpe(t, type)) owningIdx = i;
            }
            return activeIdx >= 0 && owningIdx > activeIdx;
        }

        private static bool IsEquipTask(ISafetyTask task)
        {
            return string.IsNullOrEmpty(task.ResolveExpectedActionId()) &&
                   task.requiredPPE != null && task.requiredPPE.Count > 0;
        }

        private static bool RequiresPpe(ISafetyTask task, PPEType type)
        {
            var list = task.requiredPPE;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == type) return true;
            return false;
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

        public void ForceCompleteAllPendingTasks()
        {
            for (int i = 0; i < _sessionTasks.Count; i++)
            {
                var t = _sessionTasks[i];
                if (t.State == TaskState.NotStarted || t.State == TaskState.InProgress)
                {
                    t.State = TaskState.CompletedFailure;
                    t.CompletionTime = _timer?.ElapsedSeconds ?? 0f;
                }
            }

            _currentTask = null;
            _currentTaskIndex = -1;
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
