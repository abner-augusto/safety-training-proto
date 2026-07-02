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
        [Header("Scenario (runtime data source)")]
        [Tooltip("Single fixed name (no folder scan). Resolves Resources/Scenarios/<name> as the " +
                 "embedded default, with an optional override at persistentDataPath/scenarios/<name>.json. " +
                 "Only the file matching this exact name is loaded; other JSONs in the override folder are " +
                 "ignored. Loaded via ScenarioSource. Default 'default' => override file must be default.json.")]
        [SerializeField] private string scenarioResourceName = "default";

        [Header("Task Authoring (bake source)")]
        [Tooltip("ScriptableObject authoring source for the scenario JSON. NOT used at runtime " +
                 "(the loaded JSON is). Kept to bake/re-bake the JSON and as a resilience fallback " +
                 "if the JSON fails to load. Removed in a later phase once authoring moves out of Unity.")]
        public List<TaskGroup> taskGroups = new List<TaskGroup>();

        public bool startTasksAutomatically = true;
        public float delayBetweenTasks = 2.0f;

        [Header("Timing")]
        [SerializeField] private TimerSystem? timerSystem;

        private TaskManagerCore? _core;

        /// <summary>The groups actually driving this session (from JSON, or the SO fallback).</summary>
        private IReadOnlyList<ITaskGroup> _runtimeGroups = new List<ITaskGroup>();
        public IReadOnlyList<ITaskGroup> RuntimeGroups => _runtimeGroups;

        public int CurrentTaskIndex => _core?.CurrentTaskIndex ?? -1;
        public RuntimeSafetyTask? CurrentRuntimeTask => _core?.CurrentRuntimeTask;
        public SessionCompletedEventArgs? LastSessionSummary => _core?.LastSessionSummary;

        private UnityEngine.Events.UnityAction<SessionCompletedEventArgs>? _onSessionCompleted;

        private void Start()
        {
            if (!this.IsEventBusReady()) return;

            IScoreService scoreService = ScoreService.Instance;

            _runtimeGroups = LoadRuntimeGroups();
            ValidateActions();

            if (timerSystem == null)
            {
                timerSystem = FindFirstObjectByType<TimerSystem>();
            }

            ITimerSource? timerSource = timerSystem != null
                ? new TimerSystemAdapter(timerSystem)
                : null;

            IAsyncScheduler scheduler = new AwaitableAsyncSchedulerAdapter();

            _core = new TaskManagerCore(
                bus: EventBus.Instance!,
                scoreService: scoreService,
                taskGroups: _runtimeGroups,
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

        /// <summary>
        /// Resolves the runtime groups from the unified scenario JSON (layered, fail-safe).
        /// Falls back to the SO authoring list only if the JSON can't be loaded, so a missing
        /// or corrupt scenario never leaves the session empty during the migration.
        /// </summary>
        private IReadOnlyList<ITaskGroup> LoadRuntimeGroups()
        {
            var scenario = ScenarioSource.Load(scenarioResourceName);
            if (scenario != null)
            {
                return (IReadOnlyList<ITaskGroup>)scenario.Groups;
            }

            var fallback = new List<ITaskGroup>(taskGroups.Count);
            for (int i = 0; i < taskGroups.Count; i++)
            {
                if (taskGroups[i] != null) fallback.Add(taskGroups[i]);
            }
            SafetyLog.Warning(
                $"[TaskManager] Cenário '{scenarioResourceName}' indisponível; usando os ScriptableObjects " +
                $"de autoria como fallback ({fallback.Count} grupos).", this);
            return fallback;
        }

        private void ValidateActions()
        {
            if (_runtimeGroups == null) return;
            foreach (var group in _runtimeGroups)
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

        public ITaskGroup? GetCurrentGroup() => _core?.GetCurrentGroup();

        public RuntimeSafetyTask? FindPendingTaskByActionId(string actionId) =>
            _core?.FindPendingTaskByActionId(actionId);

        public bool IsPpeAheadOfCurrentStep(PPEType type) =>
            _core?.IsPpeAheadOfCurrentStep(type) ?? false;

        public void FocusTask(RuntimeSafetyTask runtimeTask) => _core?.FocusTask(runtimeTask);

        public void RegisterOrderViolation(string description) => _core?.RegisterOrderViolation(description);

        public void ResetSession() => _core?.ResetSession();
    }
}
