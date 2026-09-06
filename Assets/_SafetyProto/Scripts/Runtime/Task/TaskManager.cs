#nullable enable
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Scenarios;
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

        public bool startTasksAutomatically = true;
        public float delayBetweenTasks = 2.0f;

        [Header("Timing")]
        [SerializeField] private TimerSystem? timerSystem;

        private TaskManagerCore? _core;

        /// <summary>The groups actually driving this session, loaded from JSON.</summary>
        private IReadOnlyList<ITaskGroup> _runtimeGroups = new List<ITaskGroup>();
        public IReadOnlyList<ITaskGroup> RuntimeGroups => _runtimeGroups;
        public string ScenarioResourceName => scenarioResourceName;
        public ScenarioDef? LoadedScenario { get; private set; }

        public int CurrentTaskIndex => _core?.CurrentTaskIndex ?? -1;
        public RuntimeSafetyTask? CurrentRuntimeTask => _core?.CurrentRuntimeTask;
        public SessionCompletedEventArgs? LastSessionSummary => _core?.LastSessionSummary;

        private UnityEngine.Events.UnityAction<SessionCompletedEventArgs>? _onSessionCompleted;
        private UnityEngine.Events.UnityAction<SessionStartedEventArgs>? _onSessionStarted;
        private bool _tasksStarted;

        public static TaskManager? Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            // Load groups in Awake (before any Start) so TotalTaskCount is available to whoever
            // stamps SessionStarted, regardless of script execution order.
            _runtimeGroups = LoadRuntimeGroups();
        }

        /// <summary>Total number of tasks across all groups in the loaded scenario.</summary>
        public int TotalTaskCount
        {
            get
            {
                int count = 0;
                foreach (var group in _runtimeGroups)
                    if (group?.tasks != null) count += group.tasks.Count;
                return count;
            }
        }

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

            if (timerSystem != null)
            {
                timerSystem.onTimerTimeout.AddListener(OnGroupTimerTimeout);
            }

            // Start tasks only once the session has actually begun, so GroupStarted/TaskStarted are
            // stamped with the session context. In the name-entry flow BeginSession is deferred until
            // the participant id is captured; starting here (scene load) would emit orphan lifecycle
            // events with empty session ids. The EventBus is a deferred queue, so a SessionStarted
            // raised during another component's Start() is still dispatched after this listener is set.
            if (startTasksAutomatically)
            {
                _onSessionStarted = _ => StartTasksOnce();
                EventBus.Instance!.onSessionStarted.AddListener(_onSessionStarted);
            }
        }

        /// <summary>Kicks off the first group/task exactly once (SessionStarted may only fire once per
        /// scene load, but this guards against any re-raise).</summary>
        private void StartTasksOnce()
        {
            if (_tasksStarted) return;
            _tasksStarted = true;
            _core?.StartSession();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (EventBus.Instance != null && _onSessionCompleted != null)
                EventBus.Instance.onSessionCompleted.RemoveListener(_onSessionCompleted);

            if (EventBus.Instance != null && _onSessionStarted != null)
                EventBus.Instance.onSessionStarted.RemoveListener(_onSessionStarted);

            if (timerSystem != null)
            {
                timerSystem.onTimerTimeout.RemoveListener(OnGroupTimerTimeout);
            }

            _core?.Dispose();
            _core = null;
        }

        /// <summary>
        /// Bridges the Runtime timer (Unity-only UnityEvent, previously observed only by
        /// TimerUI for cosmetic red-text feedback) into the domain layer so a group timeout
        /// actually drives task/group/session state forward. See
        /// <see cref="TaskManagerCore.HandleGroupTimeout"/> for the orchestration this triggers.
        /// </summary>
        private void OnGroupTimerTimeout() => _core?.HandleGroupTimeout();

        /// <summary>Scoring economy of the loaded scenario (defaults when no scenario).</summary>
        public ScoringConfig Scoring { get; private set; } = ScoringConfig.Default;

        /// <summary>Resolves runtime groups from the unified scenario JSON.</summary>
        private IReadOnlyList<ITaskGroup> LoadRuntimeGroups()
        {
            var scenario = ScenarioSource.Load(scenarioResourceName);
            if (scenario != null)
            {
                LoadedScenario = scenario;
                Scoring = scenario.Scoring ?? ScoringConfig.Default;
                return (IReadOnlyList<ITaskGroup>)scenario.Groups;
            }

            SafetyLog.Error(
                $"[TaskManager] Cenário '{scenarioResourceName}' indisponível e o runtime é 100% JSON " +
                "(sem fallback para ScriptableObjects). A sessão iniciará sem grupos.", this);
            return new List<ITaskGroup>();
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

        public ITaskGroup? GetCurrentGroup() => _core?.GetCurrentGroup();

        public RuntimeSafetyTask? FindPendingTaskByActionId(string actionId) =>
            _core?.FindPendingTaskByActionId(actionId);

        public bool IsPpeAheadOfCurrentStep(PPEType type) =>
            _core?.IsPpeAheadOfCurrentStep(type) ?? false;

        public void ResetSession() => _core?.ResetSession();

        public IReadOnlyList<RuntimeSafetyTask> CloseCurrentGroup() =>
            _core?.CloseCurrentGroup() ?? new List<RuntimeSafetyTask>();

        public IReadOnlyList<string> GetCompletionOrderDeviations(string groupId) =>
            _core?.GetCompletionOrderDeviations(groupId) ?? new List<string>();

        public bool IsGroupCompleted(string groupId) => _core?.IsGroupCompleted(groupId) ?? false;

        public ITaskGroup? FindGroup(string groupId) => _core?.FindGroupById(groupId);

        public void RegisterOrderViolation(string description) =>
            _core?.RegisterOrderViolation(description);
    }
}
