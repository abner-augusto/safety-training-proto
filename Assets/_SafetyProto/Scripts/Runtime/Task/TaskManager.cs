#nullable enable
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Tasks;
using SafetyProto.Runtime.Actions;
using SafetyProto.Utils;
using UnityEngine;

using RuntimeSafetyTask = SafetyProto.Core.RuntimeSafetyTask;

namespace SafetyProto.Runtime.Task
{
    public class TaskManager : MonoBehaviour, ISessionResettable
    {
        [Header("Task Configuration")]
        public List<TaskGroup> taskGroups = new List<TaskGroup>();
        public bool startTasksAutomatically = true;
        public float delayBetweenTasks = 2.0f;

        [Header("Timing")]
        [SerializeField] private TimerSystem? timerSystem;

        private TaskManagerCore? _core;

        public int CurrentTaskIndex => _core?.CurrentTaskIndex ?? -1;
        public RuntimeSafetyTask? CurrentRuntimeTask => _core?.CurrentRuntimeTask;
        public SessionCompletedEventArgs? LastSessionSummary => _core?.LastSessionSummary;

        private UnityEngine.Events.UnityAction<SessionCompletedEventArgs>? _onSessionCompleted;

        private void Start()
        {
            if (!this.IsEventBusReady()) return;

            IScoreService scoreService = ScoreService.Instance;

            ValidateActions();

            if (timerSystem == null)
            {
                timerSystem = FindFirstObjectByType<TimerSystem>();
            }

            ITimerSource? timerSource = timerSystem != null
                ? new TimerSystemAdapter(timerSystem)
                : null;

            IAsyncScheduler scheduler = new AwaitableAsyncSchedulerAdapter();

            var groupsAsInterface = new List<ITaskGroup>(taskGroups.Count);
            for (int i = 0; i < taskGroups.Count; i++)
            {
                groupsAsInterface.Add(taskGroups[i]);
            }

            _core = new TaskManagerCore(
                bus: EventBus.Instance!,
                scoreService: scoreService,
                taskGroups: groupsAsInterface,
                timer: timerSource,
                scheduler: scheduler,
                logger: new SafetyLogAdapter(),
                delayBetweenTasks: delayBetweenTasks);

            _core.Subscribe();

            _onSessionCompleted = _ => _core?.ForceCompleteAllPendingTasks();
            EventBus.Instance!.onSessionCompleted.AddListener(_onSessionCompleted);

            if (startTasksAutomatically)
            {
                _core.StartSession();
            }
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null && _onSessionCompleted != null)
                EventBus.Instance.onSessionCompleted.RemoveListener(_onSessionCompleted);

            _core?.Dispose();
            _core = null;
        }

        private void ValidateActions()
        {
            if (taskGroups == null) return;
            foreach (var group in taskGroups)
            {
                if (group == null || group.tasks == null) continue;
                foreach (var task in group.tasks)
                {
                    if (task == null) continue;
                    var actionId = task.ResolveExpectedActionId();
                    if (string.IsNullOrEmpty(actionId))
                    {
                        // Equip-set tasks intentionally have no action — they complete on PPE
                        // state. Only flag a task that has neither an action nor any requiredPPE.
                        if (task.requiredPPE == null || task.requiredPPE.Count == 0)
                            SafetyLog.Error($"[TaskManager] Task '{task.taskName}' has no expected action id.", this);
                        continue;
                    }
                    if (!ActionResolver.TryResolve(actionId, out _))
                    {
                        SafetyLog.Error($"[TaskManager] Action '{actionId}' for task '{task.taskName}' not found in registry.", this);
                    }
                }
            }
        }

        public IReadOnlyList<RuntimeSafetyTask> GetSessionTasks() =>
            _core?.GetSessionTasks() ?? new List<RuntimeSafetyTask>();

        public ISafetyTask? GetCurrentTaskData() => _core?.CurrentRuntimeTask?.TaskData;

        public TaskGroup? GetCurrentGroup() => _core?.GetCurrentGroup() as TaskGroup;

        public RuntimeSafetyTask? FindPendingTaskByActionId(string actionId) =>
            _core?.FindPendingTaskByActionId(actionId);

        public bool IsPpeAheadOfCurrentStep(PPEType type) =>
            _core?.IsPpeAheadOfCurrentStep(type) ?? false;

        public void FocusTask(RuntimeSafetyTask runtimeTask) => _core?.FocusTask(runtimeTask);

        public void RegisterOrderViolation(string description) => _core?.RegisterOrderViolation(description);

        public void ResetSession() => _core?.ResetSession();
    }
}
