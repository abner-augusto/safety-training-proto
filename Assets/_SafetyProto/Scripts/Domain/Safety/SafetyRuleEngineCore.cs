#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Core.Events;

namespace SafetyProto.Domain.Safety
{
    public sealed class SafetyRuleEngineCore : IDisposable
    {
        private readonly IEventBus _bus;
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
            ITimerSource? timer = null,
            IHarnessLogger? logger = null,
            bool verboseLogging = false)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
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

                // An equip-set task may already be satisfied at group start (PPE worn earlier).
                for (int i = _activeFreeOrderTasks.Count - 1; i >= 0; i--)
                    TryCompleteEquipTask(_activeFreeOrderTasks[i]);
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

                // If this newly active task is an equip-set task whose PPE are already worn
                // (player equipped them during an earlier task), complete it right away.
                TryCompleteEquipTask(_activeSequentialTask);
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

            // Equip-set tasks (no expected action, only requiredPPE) complete on PPE state,
            // not on an action attempt — so the member items can be equipped in any order
            // even inside a Sequential group (e.g. left/right gloves as one "wear gloves" task).
            if (!args.IsWearing || _activeGroup == null) return;

            if (_activeGroup.executionMode == TaskExecutionModeShared.Sequential)
            {
                TryCompleteEquipTask(_activeSequentialTask);
            }
            else
            {
                for (int i = _activeFreeOrderTasks.Count - 1; i >= 0; i--)
                    TryCompleteEquipTask(_activeFreeOrderTasks[i]);
            }
        }

        /// <summary>
        /// An equip-set task carries no expected action — only a <c>requiredPPE</c> set. It
        /// completes the moment every item in that set is worn, regardless of equip order. This
        /// is what lets a single "wear gloves" task accept left/right in any sequence.
        /// </summary>
        private static bool IsEquipTask(ISafetyTask? task)
        {
            return task != null &&
                   string.IsNullOrEmpty(task.ResolveExpectedActionId()) &&
                   task.requiredPPE != null && task.requiredPPE.Count > 0;
        }

        private bool TryCompleteEquipTask(ISafetyTask? task)
        {
            if (_activeGroup == null || !IsEquipTask(task)) return false;
            if (!IsPpeCompliant(task!.requiredPPE)) return false;

            ProcessTaskAttempt(task!, _activeGroup);

            // Stop a later PPE event from re-completing the same sequential task before the
            // next OnTaskStarted reassigns the active reference. (FreeOrder is already guarded
            // by ProcessTaskAttempt removing the task from _activeFreeOrderTasks.)
            if (ReferenceEquals(_activeSequentialTask, task))
                _activeSequentialTask = null;

            return true;
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
            bool compliant = IsPpeCompliant(task.requiredPPE);

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

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed)
            {
                WasPpeCompliant = compliant
            });
        }

        private bool IsPpeCompliant(IReadOnlyCollection<PPEType>? requiredPpe)
        {
            if (requiredPpe == null || requiredPpe.Count == 0)
                return true;

            // Use the event-driven state cache so compliance is checked against
            // the PPE state consistent with the current event-processing order.
            // Querying PPEManager directly races against physics callbacks that
            // update _wornPPE before the EventBus dispatches PpeStateChanged events.
            foreach (var ppe in requiredPpe)
            {
                if (!_ppeStates.TryGetValue(ppe, out var isWearing) || !isWearing)
                    return false;
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

        public void Dispose()
        {
            if (_disposed) return;
            Unsubscribe();
            _disposed = true;
        }
    }
}
