#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Data.Enums;
using SafetyProto.Core.Events;
using SafetyProto.Runtime.Task;

namespace SafetyProto.Domain.Safety
{
    public sealed class SafetyRuleEngineCore : IDisposable
    {
        private readonly IEventBus _bus;
        private readonly IPPEComplianceChecker? _ppeChecker;
        private readonly ITimerSource? _timer;
        private readonly IHarnessLogger? _logger;
        private readonly bool _verboseLogging;

        private readonly Dictionary<PPEType, bool> _ppeStates = new Dictionary<PPEType, bool>();
        private ITaskGroup? _activeGroup;
        private ISafetyTask? _activeSequentialTask;
        private readonly List<ISafetyTask> _activeFreeOrderTasks = new List<ISafetyTask>();

        private readonly Action<ActionAttemptedEvent>    _onActionAttempt;
        private readonly Action<PPEStateChangedEventArgs> _onPpeStateChanged;
        private readonly Action<TaskGroupEventArgs>       _onGroupLifecycle;
        private readonly Action<TaskEventArgs>            _onTaskLifecycle;

        private bool _subscribed;
        private bool _disposed;

        public SafetyRuleEngineCore(
            IEventBus bus,
            IPPEComplianceChecker? ppeChecker = null,
            ITimerSource? timer = null,
            IHarnessLogger? logger = null,
            bool verboseLogging = false)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _ppeChecker = ppeChecker;
            _timer = timer;
            _logger = logger;
            _verboseLogging = verboseLogging;

            _onActionAttempt = HandleActionAttempt;
            _onPpeStateChanged = HandlePpeStateChanged;
            _onGroupLifecycle = HandleGroupLifecycle;
            _onTaskLifecycle = HandleTaskLifecycle;
        }

        public void Subscribe()
        {
            if (_subscribed) return;
            _bus.Subscribe(_onActionAttempt);
            _bus.Subscribe(_onPpeStateChanged);
            _bus.Subscribe(_onGroupLifecycle);
            _bus.Subscribe(_onTaskLifecycle);
            _subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!_subscribed) return;
            _bus.Unsubscribe(_onActionAttempt);
            _bus.Unsubscribe(_onPpeStateChanged);
            _bus.Unsubscribe(_onGroupLifecycle);
            _bus.Unsubscribe(_onTaskLifecycle);
            _subscribed = false;
        }

        private void HandleGroupLifecycle(TaskGroupEventArgs args)
        {
            switch (args.Phase)
            {
                case TaskGroupPhase.Started:
                    OnGroupStarted(args);
                    break;
                case TaskGroupPhase.Completed:
                    OnGroupCompleted(args);
                    break;
            }
        }

        private void HandleTaskLifecycle(TaskEventArgs args)
        {
            if (args.Phase == TaskPhase.Started)
            {
                OnTaskStarted(args);
            }
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            _activeGroup = args.Group;
            _activeSequentialTask = null;
            _activeFreeOrderTasks.Clear();

            if (_activeGroup != null && _activeGroup.executionMode == TaskExecutionModeShared.FreeOrder)
            {
                _activeFreeOrderTasks.AddRange(_activeGroup.tasks);
            }
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            if (ReferenceEquals(_activeGroup, args.Group))
            {
                ClearActiveContext();
            }
        }

        private void OnTaskStarted(TaskEventArgs args)
        {
            if (_activeGroup == null || args.Task == null) return;

            if (_activeGroup.executionMode == TaskExecutionModeShared.Sequential)
            {
                _activeSequentialTask = args.Task;
            }
        }

        private void ClearActiveContext()
        {
            _activeGroup = null;
            _activeSequentialTask = null;
            _activeFreeOrderTasks.Clear();
        }

        private void HandlePpeStateChanged(PPEStateChangedEventArgs args)
        {
            _ppeStates[args.PpeType] = args.IsWearing;
        }

        private void HandleActionAttempt(ActionAttemptedEvent args)
        {
            var actionId = args.ActionId;
            if (string.IsNullOrWhiteSpace(actionId))
            {
                RaiseViolation("ACTION_ID_MISSING", "Received action attempt without a valid ActionId.", null, null);
                return;
            }

            actionId = actionId.Trim();

            if (_activeGroup == null)
            {
                RaiseViolation("NO_ACTIVE_GROUP", "Action attempted with no active task group.", null, null);
                return;
            }

            ISafetyTask? targetTask = null;

            if (_activeGroup.executionMode == TaskExecutionModeShared.Sequential)
            {
                if (_activeSequentialTask == null)
                {
                    if (_verboseLogging)
                    {
                        _logger?.Warning("SafetyRuleEngineCore: Sequential group active but no current task set.");
                    }
                    return;
                }

                if (!MatchesAction(_activeSequentialTask, actionId))
                {
                    RaiseViolation(
                        "WRONG_ACTION",
                        $"Expected {_activeSequentialTask.ResolveExpectedActionId()} but received {actionId}.",
                        _activeSequentialTask,
                        _activeGroup);
                    return;
                }

                targetTask = _activeSequentialTask;
            }
            else
            {
                targetTask = _activeFreeOrderTasks.FirstOrDefault(t => MatchesAction(t, actionId));
                if (targetTask == null)
                {
                    if (IsActionAlreadyCompleted(actionId))
                    {
                        if (_verboseLogging)
                        {
                            _logger?.Info($"SafetyRuleEngineCore: Ignoring repeat action {actionId} (already completed).");
                        }
                        return;
                    }

                    RaiseViolation(
                        "WRONG_ACTION",
                        $"Action {actionId} does not match any pending task in '{_activeGroup.groupName}'.",
                        null,
                        _activeGroup);
                    return;
                }
            }

            ProcessTaskAttempt(targetTask, _activeGroup);
        }

        private bool IsActionAlreadyCompleted(string actionId)
        {
            if (_activeGroup == null || string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            return _activeGroup.tasks.Any(t => MatchesAction(t, actionId)) &&
                   _activeFreeOrderTasks.All(t => !MatchesAction(t, actionId));
        }

        private void ProcessTaskAttempt(ISafetyTask task, ITaskGroup currentGroup)
        {
            var runtimeTask = new RuntimeSafetyTask(task);

            bool compliant = IsPpeCompliant(task.requiredPPE);
            runtimeTask.State = compliant ? TaskState.CompletedSuccess : TaskState.CompletedSuccessButUnsafe;
            runtimeTask.HasMissedPPEOnce = !compliant;
            runtimeTask.CompletionTime = _timer?.ElapsedSeconds ?? 0f;

            if (!compliant)
            {
                RaiseViolation(
                    "PPE_MISSING",
                    $"Required PPE missing for task '{task.taskName}'.",
                    task,
                    currentGroup);
            }

            if (_verboseLogging)
            {
                _logger?.Info($"SafetyRuleEngineCore: Task '{task.taskName}' completed. PPE compliant={compliant}");
            }

            if (currentGroup.executionMode == TaskExecutionModeShared.FreeOrder)
            {
                _activeFreeOrderTasks.Remove(task);
            }

            _bus.Publish(new TaskEventArgs(task, runtimeTask, TaskPhase.Completed));
        }

        private bool IsPpeCompliant(IReadOnlyCollection<PPEType>? requiredPpe)
        {
            if (requiredPpe == null || requiredPpe.Count == 0)
            {
                return true;
            }

            if (_ppeChecker != null)
            {
                return _ppeChecker.IsCompliant(requiredPpe);
            }

            foreach (var ppe in requiredPpe)
            {
                if (!_ppeStates.TryGetValue(ppe, out var isWearing) || !isWearing)
                {
                    return false;
                }
            }

            return true;
        }

        private void RaiseViolation(string code, string message, ISafetyTask? task, ITaskGroup? group)
        {
            _bus.Publish(new SafetyViolationEventArgs
            {
                ViolationCode = code,
                Message = message,
                TaskId = task != null ? task.taskName : string.Empty,
                GroupId = group != null ? group.groupName : string.Empty
            });
        }

        private static bool MatchesAction(ISafetyTask? task, string actionId)
        {
            if (task == null || string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            var expectedId = task.ResolveExpectedActionId();
            return !string.IsNullOrEmpty(expectedId) &&
                   string.Equals(expectedId, actionId, StringComparison.OrdinalIgnoreCase);
        }

        public void NotifyGroupStarted(TaskGroupEventArgs args) => HandleGroupLifecycle(args);

        public void NotifyGroupCompleted(TaskGroupEventArgs args) => HandleGroupLifecycle(args);

        public void NotifyTaskStarted(TaskEventArgs args) => HandleTaskLifecycle(args);

        public void Dispose()
        {
            if (_disposed) return;
            Unsubscribe();
            _disposed = true;
        }
    }
}
