#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Core.Events;
using SafetyProto.Domain.Tasks;

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

        /// <summary>Ids completed inside the active group, used to resolve a group's
        /// prerequisite independently of execution mode. Cleared on every group start.</summary>
        private readonly HashSet<string> _completedTaskIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            else if (args.Phase == TaskPhase.Completed && args.Task != null)
            {
                // Also covers completions this engine did not drive, so a prerequisite
                // satisfied elsewhere still unlocks its siblings. Timeout is deliberately
                // not recorded: a prerequisite the participant never satisfied must keep
                // blocking, and the group time limit is what ends the group.
                _completedTaskIds.Add(args.Task.id);
            }
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            _activeGroup = args.Group;
            _activeSequentialTask = null;
            _activeFreeOrderTasks.Clear();
            _completedTaskIds.Clear();

            if (_activeGroup != null && TaskExecutionRules.EffectiveMode(_activeGroup) == TaskExecutionModeShared.FreeOrder)
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

            if (TaskExecutionRules.EffectiveMode(_activeGroup) == TaskExecutionModeShared.Sequential)
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
            _completedTaskIds.Clear();
        }

        private void HandlePpeStateChanged(PPEStateChangedEventArgs args)
        {
            _ppeStates[args.PpeType] = args.IsWearing;

            // Equip-set tasks (no expected action, only requiredPPE) complete on PPE state,
            // not on an action attempt — so the member items can be equipped in any order
            // even inside a Sequential group (e.g. left/right gloves as one "wear gloves" task).
            if (!args.IsWearing || _activeGroup == null) return;

            if (TaskExecutionRules.EffectiveMode(_activeGroup) == TaskExecutionModeShared.Sequential)
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
        private bool TryCompleteEquipTask(ISafetyTask? task)
        {
            if (_activeGroup == null || !TaskExecutionRules.IsEquipTask(task)) return false;
            if (!IsPpeCompliant(task!.requiredPPE)) return false;

            if (!ProcessTaskAttempt(task!, _activeGroup)) return false;

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
                RaiseViolation("ACTION_ID_MISSING", "Tentativa de ação recebida sem identificação válida.", null, null);
                return;
            }

            actionId = actionId.Trim();

            if (_activeGroup == null)
            {
                RaiseViolation("NO_ACTIVE_GROUP", "Ação realizada sem um grupo de tarefas ativo.", null, null);
                return;
            }

            ISafetyTask? targetTask = null;

            if (TaskExecutionRules.EffectiveMode(_activeGroup) == TaskExecutionModeShared.Sequential)
            {
                if (_activeSequentialTask == null)
                {
                    if (_verboseLogging)
                    {
                        _logger?.Warning("SafetyRuleEngineCore: Sequential group active but no current task set.");
                    }
                    return;
                }

                if (!TaskExecutionRules.MatchesAction(_activeSequentialTask, actionId))
                {
                    RaiseViolation(
                        "WRONG_ACTION",
                        $"A tarefa esperada era '{_activeSequentialTask.taskName}', mas outra ação foi realizada.",
                        _activeSequentialTask,
                        _activeGroup);
                    return;
                }

                targetTask = _activeSequentialTask;
            }
            else
            {
                targetTask = _activeFreeOrderTasks.FirstOrDefault(t => TaskExecutionRules.MatchesAction(t, actionId));
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
                        $"A ação realizada não corresponde a nenhuma tarefa pendente do grupo '{_activeGroup.groupName}'.",
                        null,
                        _activeGroup);
                    return;
                }
            }

            if (ProcessTaskAttempt(targetTask, _activeGroup))
            {
                ReleaseEquipTasksAfterPrerequisite(targetTask);
            }
        }

        private bool IsActionAlreadyCompleted(string actionId)
        {
            if (_activeGroup == null || string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            return _activeGroup.tasks.Any(t => TaskExecutionRules.MatchesAction(t, actionId)) &&
                   _activeFreeOrderTasks.All(t => !TaskExecutionRules.MatchesAction(t, actionId));
        }

        /// <summary>
        /// True when <paramref name="task"/> must be refused because its group names another
        /// task as the safety precondition for the whole group and that task is still pending.
        /// </summary>
        /// <remarks>
        /// Guided only. In Evaluation the participant has to be free to work unanchored so the
        /// inspection gate's consequences can measure the omission — blocking there would hide
        /// exactly the behaviour the evaluation is looking for.
        /// The check is mode-independent otherwise: it reads completion, not the pending list,
        /// so a Sequential group behaves the same. Free order is untouched — a refused task
        /// stays pending and every sibling remains available in any order once the
        /// precondition is met.
        /// </remarks>
        private bool IsBlockedByPrerequisite(ISafetyTask task, ITaskGroup group)
        {
            if (SessionModeState.Current != SessionMode.Guided) return false;

            var prerequisiteId = group.prerequisiteTaskId;
            if (string.IsNullOrWhiteSpace(prerequisiteId)) return false;
            if (string.Equals(task.id, prerequisiteId, StringComparison.OrdinalIgnoreCase)) return false;
            if (_completedTaskIds.Contains(prerequisiteId)) return false;

            // ScenarioLoader rejects an unresolvable id, but a host that builds groups in code
            // could still pass one. Warn and let the group run rather than deadlock it.
            if (FindTask(group, prerequisiteId) == null)
            {
                _logger?.Warning(
                    $"SafetyRuleEngineCore: prerequisiteTaskId '{prerequisiteId}' não existe no grupo " +
                    $"'{group.groupName}'. Pré-requisito ignorado.");
                return false;
            }

            return true;
        }

        private static ISafetyTask? FindTask(ITaskGroup group, string taskId) =>
            group.tasks.FirstOrDefault(t =>
                string.Equals(t.id, taskId, StringComparison.OrdinalIgnoreCase));

        private void RaisePrerequisitePending(ISafetyTask blockedTask, ITaskGroup group)
        {
            var prerequisite = FindTask(group, group.prerequisiteTaskId);
            var pendingName = prerequisite != null ? prerequisite.taskName : group.prerequisiteTaskId;

            var message = !string.IsNullOrWhiteSpace(group.prerequisiteAdvice)
                ? group.prerequisiteAdvice
                : $"Conclua '{pendingName}' antes de executar as outras tarefas deste grupo.";

            // TaskId is the REFUSED task: the analysis question is what the participant tried
            // to do before satisfying the precondition.
            RaiseViolation("PREREQUISITE_PENDING", message, blockedTask, group);
        }

        /// <summary>
        /// Completes <paramref name="task"/> unless its group's precondition blocks it.
        /// Returns false when the attempt was refused and the task is still pending.
        /// </summary>
        private bool ProcessTaskAttempt(ISafetyTask task, ITaskGroup currentGroup)
        {
            if (IsBlockedByPrerequisite(task, currentGroup))
            {
                RaisePrerequisitePending(task, currentGroup);

                if (_verboseLogging)
                {
                    _logger?.Info(
                        $"SafetyRuleEngineCore: Task '{task.taskName}' refused — prerequisite " +
                        $"'{currentGroup.prerequisiteTaskId}' still pending.");
                }

                return false;
            }

            bool compliant = IsPpeCompliant(task.requiredPPE);

            if (!compliant)
            {
                RaiseViolation(
                    "PPE_MISSING",
                    $"Faltam EPIs obrigatórios para a tarefa '{task.taskName}'.",
                    task,
                    currentGroup);
            }

            if (_verboseLogging)
            {
                _logger?.Info($"SafetyRuleEngineCore: Task '{task.taskName}' completed. PPE compliant={compliant}");
            }

            if (TaskExecutionRules.EffectiveMode(currentGroup) == TaskExecutionModeShared.FreeOrder)
            {
                _activeFreeOrderTasks.Remove(task);
            }

            _completedTaskIds.Add(task.id);

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed)
            {
                WasPpeCompliant = compliant
            });

            return true;
        }

        /// <summary>
        /// Re-runs the equip-set sweep after the group's precondition is met. An equip-set task
        /// completes on a PPE state change, so one refused while the precondition was pending
        /// would never get a second chance — the items are already worn and no further
        /// PpeStateChanged is coming. Only groups that mix a precondition with equip-set tasks
        /// need this; it is a no-op otherwise.
        /// </summary>
        private void ReleaseEquipTasksAfterPrerequisite(ISafetyTask completedTask)
        {
            if (_activeGroup == null) return;
            if (string.IsNullOrWhiteSpace(_activeGroup.prerequisiteTaskId)) return;
            if (!string.Equals(completedTask.id, _activeGroup.prerequisiteTaskId,
                    StringComparison.OrdinalIgnoreCase)) return;

            if (TaskExecutionRules.EffectiveMode(_activeGroup) == TaskExecutionModeShared.FreeOrder)
            {
                for (int i = _activeFreeOrderTasks.Count - 1; i >= 0; i--)
                    TryCompleteEquipTask(_activeFreeOrderTasks[i]);
            }
            else
            {
                TryCompleteEquipTask(_activeSequentialTask);
            }
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
                TaskId = task != null ? task.id : string.Empty,
                GroupId = group != null ? group.id : string.Empty,
                TaskName = task != null ? task.taskName : string.Empty,
                GroupName = group != null ? group.groupName : string.Empty
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            Unsubscribe();
            _disposed = true;
        }
    }
}
