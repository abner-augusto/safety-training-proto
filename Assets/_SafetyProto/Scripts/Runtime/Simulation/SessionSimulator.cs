#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scenarios;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime;
using SafetyProto.Runtime.Safety;
using SafetyProto.Runtime.Session;
using SafetyProto.Runtime.Task;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Runtime.Simulation
{
    public enum SimulationStatus
    {
        Idle,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    [Serializable]
    public sealed class SimulationTaskSnapshot
    {
        public string id = string.Empty;
        public string name = string.Empty;
        public string groupId = string.Empty;
        public string state = string.Empty;
    }

    [Serializable]
    public sealed class SessionSimulationResult
    {
        public SimulationStatus status;
        public int currentStep = -1;
        public string activeGroup = string.Empty;
        public string participantId = string.Empty;
        public string lastDiagnostic = string.Empty;
        public int score;
        public SessionCompletedEventArgs? sessionSummary;
        public readonly List<SimulationTaskSnapshot> tasks = new List<SimulationTaskSnapshot>();
        public readonly List<string> transcript = new List<string>();
        public readonly List<string> consequences = new List<string>();

        public string FormatStatus()
        {
            return $"{status} | etapa {Mathf.Max(0, currentStep + 1)} | grupo: " +
                   (string.IsNullOrEmpty(activeGroup) ? "nenhum" : activeGroup) +
                   $" | score: {score}";
        }
    }

    /// <summary>
    /// Editor/Development-only semantic actor. It feeds the real event facades and public gates;
    /// it does not emulate hands, physics, or domain rules. SIM- identities never enter the private
    /// participant-name mapping and are the exclusion contract for simulator data.
    /// </summary>
    public sealed class SessionSimulator : MonoBehaviour
    {
        private const float DefaultInputTimeoutSeconds = 8f;
        private const float DefaultOperationTimeoutSeconds = 45f;
        private static SessionSimulator? _instance;

        [SerializeField] private string externalScenarioPath = string.Empty;
        [SerializeField] private SessionMode mode = SessionMode.Guided;
        [SerializeField] private float inputTimeoutSeconds = DefaultInputTimeoutSeconds;
        [SerializeField] private float operationTimeoutSeconds = DefaultOperationTimeoutSeconds;

        private TrainingSessionManager? _sessionManager;
        private TaskManager? _taskManager;
        private PhaseAdvanceGate? _phaseGate;
        private PhaseController? _phaseController;
        private InspectionGateValidator? _inspectionGate;
        private ScenarioDef? _scenario;
        private int _nextStep;
        private bool _prepared;
        private bool _sessionStarted;
        private bool _sessionCompleted;
        private bool _inputObserved;
        private Coroutine? _routine;
        private bool _busy;
        private readonly SessionSimulationResult _result = new SessionSimulationResult();

        private UnityAction<SessionStartedEventArgs>? _onSessionStarted;
        private UnityAction<SessionCompletedEventArgs>? _onSessionCompleted;
        private UnityAction<SessionEndedEventArgs>? _onSessionEnded;
        private UnityAction<TaskGroupEventArgs>? _onGroupStarted;
        private UnityAction<TaskGroupEventArgs>? _onGroupCompleted;
        private UnityAction<TaskEventArgs>? _onTaskStarted;
        private UnityAction<TaskEventArgs>? _onTaskCompleted;
        private UnityAction<PPEStateChangedEventArgs>? _onPpeStateChanged;
        private UnityAction<ActionAttemptedEvent>? _onActionAttempt;
        private UnityAction<ScoreChangedEventArgs>? _onScoreChanged;
        private UnityAction<SafetyViolationEventArgs>? _onSafetyViolation;
        private UnityAction<CriticalSafetyFailureEventArgs>? _onCriticalFailure;
        private UnityAction<SafetyErrorEventArgs>? _onSafetyError;

        public static SessionSimulator GetOrCreate()
        {
            if (_instance != null) return _instance;
            _instance = FindFirstObjectByType<SessionSimulator>();
            if (_instance != null) return _instance;

            var go = new GameObject("Session Simulator (Editor Only)")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _instance = go.AddComponent<SessionSimulator>();
            return _instance;
        }

        public SessionMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public string ExternalScenarioPath
        {
            get => externalScenarioPath;
            set => externalScenarioPath = value ?? string.Empty;
        }

        public SessionSimulationResult Result => _result;
        public bool IsRunning => _result.status == SimulationStatus.Running;
        public bool IsBusy => _busy;
        public string LoadedScenarioName => _taskManager?.LoadedScenario?.Name ?? "(não carregado)";

        public void Run()
        {
            if (_busy || (_prepared && !IsRunning)) return;
            if (!_prepared) ResetResult();
            _busy = true;
            _routine = StartCoroutine(RunAllCoroutine());
        }

        public void Step()
        {
            if (_busy || (_prepared && !IsRunning)) return;
            _busy = true;
            _routine = StartCoroutine(StepCoroutine());
        }

        public void Cancel()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            _busy = false;
            _inspectionGate?.CancelSimulationProcessing();
            if (IsRunning)
            {
                _result.status = SimulationStatus.Cancelled;
                _result.lastDiagnostic = "Simulação cancelada. Reinicie o Play Mode para iniciar outra sessão.";
                RefreshSnapshot();
            }
            Unsubscribe();
        }

        private IEnumerator RunAllCoroutine()
        {
            if (!_prepared)
                yield return PrepareCoroutine();
            if (!IsRunning)
            {
                _busy = false;
                _routine = null;
                yield break;
            }

            while (_scenario != null && _nextStep < _scenario.Script.Count)
            {
                yield return ExecuteStepCoroutine(_nextStep);
                if (!IsRunning)
                {
                    _busy = false;
                    _routine = null;
                    yield break;
                }
                _nextStep++;
            }

            if (IsRunning)
            {
                yield return WaitForTerminalSession();
                if (IsRunning) CompleteOrFailAfterTerminal();
            }
            _busy = false;
            _routine = null;
        }

        private IEnumerator StepCoroutine()
        {
            if (!_prepared)
            {
                ResetResult();
                yield return PrepareCoroutine();
            }

            if (!IsRunning || _scenario == null || _nextStep >= _scenario.Script.Count)
            {
                if (IsRunning)
                {
                    yield return WaitForTerminalSession();
                    if (IsRunning) CompleteOrFailAfterTerminal();
                }
                _busy = false;
                _routine = null;
                yield break;
            }

            yield return ExecuteStepCoroutine(_nextStep);
            if (IsRunning)
            {
                _nextStep++;
                if (_scenario != null && _nextStep >= _scenario.Script.Count)
                {
                    yield return WaitForTerminalSession();
                    if (IsRunning) CompleteOrFailAfterTerminal();
                }
            }
            _busy = false;
            _routine = null;
        }

        private IEnumerator PrepareCoroutine()
        {
            _result.status = SimulationStatus.Running;
            _result.lastDiagnostic = "Preparando sessão simulada...";
            _sessionManager = FindFirstObjectByType<TrainingSessionManager>();
            _taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindFirstObjectByType<TaskManager>();
            _phaseGate = FindFirstObjectByType<PhaseAdvanceGate>();
            _phaseController = FindFirstObjectByType<PhaseController>();
            _inspectionGate = FindFirstObjectByType<InspectionGateValidator>();
            _scenario = string.IsNullOrWhiteSpace(externalScenarioPath)
                ? _taskManager?.LoadedScenario
                : ScenarioSource.LoadFile(externalScenarioPath);

            if (_sessionManager == null || _taskManager == null)
            {
                Fail("Não foi possível localizar TrainingSessionManager e TaskManager na cena ativa.");
                yield break;
            }
            if (_sessionManager.IsSessionStarted)
            {
                Fail("A sessão real já foi iniciada. Reinicie o Play Mode antes de iniciar uma simulação.");
                yield break;
            }
            if (_scenario == null || _scenario.Script == null || _scenario.Script.Count == 0)
            {
                Fail("O cenário carregado pelo TaskManager não possui um script executável.");
                yield break;
            }
            if (!string.IsNullOrWhiteSpace(externalScenarioPath))
            {
                if (_taskManager.LoadedScenario == null)
                {
                    Fail("O TaskManager não possui um cenário carregado para validar o roteiro externo.");
                    yield break;
                }
                var compatibility = ScenarioCompatibility.Validate(_scenario, _taskManager.LoadedScenario);
                if (!compatibility.Compatible)
                {
                    Fail("Cenário externo incompatível com as tarefas carregadas: " + compatibility.ErrorSummary);
                    yield break;
                }
            }
            if (EventBus.Instance == null)
            {
                Fail("EventBus não está disponível; a simulação não pode publicar eventos.");
                yield break;
            }

            Subscribe();
            SessionModeState.Current = mode;
            _result.participantId = ParticipantIdentity.SetSimulatedParticipant();
            _inspectionGate?.SetSimulationAutoConfirm(true);
            _sessionManager.BeginSession();
            _prepared = true;

            yield return WaitUntil(() => _sessionStarted,
                "SessionStarted não foi observado dentro do tempo limite de despacho.", inputTimeoutSeconds);
            RefreshSnapshot();
        }

        private IEnumerator ExecuteStepCoroutine(int index)
        {
            if (_scenario == null || index < 0 || index >= _scenario.Script.Count) yield break;
            var step = _scenario.Script[index];
            _result.currentStep = index;
            _result.lastDiagnostic = $"Executando etapa {index + 1}: {step.Kind}";
            RefreshSnapshot();

            if (step.DelayMs > 0)
                yield return new WaitForSeconds(Mathf.Clamp(step.DelayMs / 1000f, 0f, 30f));

            switch ((step.Kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "ppe":
                    if (!Enum.TryParse(step.PpeType, true, out PPEType ppe))
                    {
                        Fail($"Etapa {index + 1}: EPI desconhecido '{step.PpeType}'.");
                        yield break;
                    }
                    string ppeTaskId = _taskManager?.CurrentRuntimeTask?.id ?? string.Empty;
                    _inputObserved = false;
                    PPEEvents.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppe, step.IsWearing));
                    yield return WaitForInputSettlement(ppeTaskId);
                    if (!IsRunning) yield break;
                    break;

                case "action":
                    if (string.IsNullOrWhiteSpace(step.ActionId))
                    {
                        Fail($"Etapa {index + 1}: actionId vazio.");
                        yield break;
                    }
                    string actionTaskId = _taskManager?.FindPendingTaskByActionId(step.ActionId)?.id ?? string.Empty;
                    _inputObserved = false;
                    ActionEvents.PublishActionAttempt(step.ActionId);
                    yield return WaitForInputSettlement(actionTaskId);
                    if (!IsRunning) yield break;
                    break;

                case "gate":
                    yield return ExecuteGate(step);
                    break;

                default:
                    Fail($"Etapa {index + 1}: comando '{step.Kind}' não suportado.");
                    yield break;
            }

            RefreshSnapshot();
        }

        private IEnumerator ExecuteGate(ScriptStepDef step)
        {
            string target = (step.GateTarget ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(target))
                target = _taskManager?.GetCurrentGroup()?.id == "ppe_selection" ? "phase1" : "inspection";

            if (target == "phase" || target == "phase1" || target == "fase1")
            {
                if (_phaseGate == null)
                {
                    Fail("Gate da fase 1 não encontrado na cena ativa.");
                    yield break;
                }
                var before = _taskManager?.GetCurrentGroup();
                _phaseController?.SetSimulationAutoConfirm(true);
                _phaseGate.Advance();
                yield return WaitUntil(() => !ReferenceEquals(before, _taskManager?.GetCurrentGroup()) &&
                    _phaseController != null && _phaseController.SimulationTransitionCompleted,
                    $"Espera da transição de fase excedeu {operationTimeoutSeconds:F0} s: o andaime não ficou pronto.",
                    operationTimeoutSeconds);
            }
            else if (target == "inspection" || target == "final" || target == "inspecao")
            {
                if (_inspectionGate == null)
                {
                    Fail("Gate de inspeção não encontrado na cena ativa.");
                    yield break;
                }
                _inspectionGate.Validate();
                yield return WaitUntil(() => _sessionCompleted && IsTerminalSnapshot(),
                    $"Espera da inspeção excedeu {operationTimeoutSeconds:F0} s: o gate não produziu SessionCompleted.",
                    operationTimeoutSeconds);
            }
            else
            {
                Fail($"Alvo de gate desconhecido '{step.GateTarget}'. Use phase1 ou inspection.");
                yield break;
            }
        }

        private IEnumerator WaitForInputSettlement(string taskId)
        {
            float elapsed = 0f;
            while (!_inputObserved && elapsed < Mathf.Max(0.5f, inputTimeoutSeconds))
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!_inputObserved)
            {
                Fail($"A entrada da etapa {_result.currentStep + 1} não foi despachada pelo EventBus dentro do tempo limite.");
                yield break;
            }

            // Wrong input is a valid observable outcome, so settlement timing must not fail it.
            elapsed = 0f;
            bool taskSettled = string.IsNullOrEmpty(taskId);
            while (elapsed < Mathf.Min(3f, Mathf.Max(0.5f, inputTimeoutSeconds)))
            {
                if (!string.IsNullOrEmpty(taskId))
                {
                    var task = FindTask(taskId);
                    if (task == null || (task.State != TaskState.NotStarted &&
                                         task.State != TaskState.InProgress))
                    {
                        taskSettled = true;
                        break;
                    }
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            // TaskManager may intentionally delay starting the next task. Do not publish
            // the next scripted input into that gap.
            if (taskSettled && !string.IsNullOrEmpty(taskId))
            {
                elapsed = 0f;
                while (!_sessionCompleted && elapsed < Mathf.Min(3f, Mathf.Max(0.5f, inputTimeoutSeconds)) &&
                       (_taskManager?.CurrentRuntimeTask == null ||
                        _taskManager.CurrentRuntimeTask.id == taskId))
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
        }

        private SafetyProto.Core.RuntimeSafetyTask? FindTask(string id)
        {
            if (_taskManager == null || string.IsNullOrEmpty(id)) return null;
            foreach (var task in _taskManager.GetSessionTasks())
                if (task.id == id) return task;
            return null;
        }

        private IEnumerator WaitUntil(Func<bool> condition, string timeoutMessage, float timeoutSeconds)
        {
            float elapsed = 0f;
            while (!condition() && elapsed < Mathf.Max(0.5f, timeoutSeconds))
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (!condition())
                Fail(timeoutMessage);
        }

        private bool IsTerminalSnapshot()
        {
            if (!_sessionCompleted || _result.sessionSummary == null) return false;
            foreach (var task in _taskManager?.GetSessionTasks() ?? Array.Empty<SafetyProto.Core.RuntimeSafetyTask>())
            {
                if (task.State == TaskState.NotStarted || task.State == TaskState.InProgress)
                    return false;
            }
            return true;
        }

        private IEnumerator WaitForTerminalSession()
        {
            yield return WaitUntil(IsTerminalSnapshot,
                $"Espera terminal excedeu {operationTimeoutSeconds:F0} s: SessionCompleted ou snapshot terminal não foi observado.",
                operationTimeoutSeconds);
        }

        private void CompleteOrFailAfterTerminal()
        {
            if (!IsTerminalSnapshot())
            {
                Fail("A sessão simulada terminou o roteiro sem uma conclusão terminal real.");
                return;
            }

            _result.status = SimulationStatus.Completed;
            _result.lastDiagnostic = "SessionCompleted observado; snapshot terminal consolidado.";
            RefreshSnapshot();
            Unsubscribe();
        }

        private void Subscribe()
        {
            var bus = EventBus.Instance;
            _onSessionStarted = _ => { _sessionStarted = true; AddTranscript("SessionStarted"); };
            _onSessionCompleted = args =>
            {
                _sessionCompleted = true;
                _result.sessionSummary = args;
                AddTranscript("SessionCompleted");
            };
            _onSessionEnded = _ => AddTranscript("SessionEnded");
            _onGroupStarted = args => AddTranscript("GroupStarted: " + (args.Group?.id ?? "<null>"));
            _onGroupCompleted = args => AddTranscript("GroupCompleted: " + (args.Group?.id ?? "<null>"));
            _onTaskStarted = args => AddTranscript("TaskStarted: " + (args.Task?.taskName ?? "<null>"));
            _onTaskCompleted = args => AddTranscript("TaskCompleted: " + (args.Task?.taskName ?? "<null>"));
            _onPpeStateChanged = args =>
            {
                _inputObserved = true;
                AddTranscript($"PPEStateChanged: {args.PpeType}={args.IsWearing}");
            };
            _onActionAttempt = args =>
            {
                _inputObserved = true;
                AddTranscript("ActionAttempt: " + args.ActionId);
            };
            _onScoreChanged = args => { _result.score = args.TotalScore; AddTranscript($"ScoreChanged: {args.TotalScore} ({args.Delta})"); };
            _onSafetyViolation = args => AddTranscript("SafetyViolation: " + args.ViolationCode);
            _onCriticalFailure = args => AddTranscript("CriticalSafetyFailure: " + args.Reason);
            _onSafetyError = args => AddTranscript("SafetyError: " + args.Message);

            bus.onSessionStarted.AddListener(_onSessionStarted);
            bus.onSessionCompleted.AddListener(_onSessionCompleted);
            bus.onSessionEnded.AddListener(_onSessionEnded);
            bus.onGroupStarted.AddListener(_onGroupStarted);
            bus.onGroupCompleted.AddListener(_onGroupCompleted);
            bus.onTaskStarted.AddListener(_onTaskStarted);
            bus.onTaskCompleted.AddListener(_onTaskCompleted);
            bus.onPpeStateChanged.AddListener(_onPpeStateChanged);
            bus.onActionAttempt.AddListener(_onActionAttempt);
            bus.onScoreChanged.AddListener(_onScoreChanged);
            bus.onSafetyViolation.AddListener(_onSafetyViolation);
            bus.onCriticalSafetyFailure.AddListener(_onCriticalFailure);
            bus.onSafetyError.AddListener(_onSafetyError);
            ConsequenceEvents.OnConsequenceStarted += OnConsequenceStarted;
            ConsequenceEvents.OnConsequenceEnded += OnConsequenceEnded;
        }

        private void Unsubscribe()
        {
            _inspectionGate?.CancelSimulationProcessing();
            _phaseController?.CancelSimulationTransition();
            _phaseController?.SetSimulationAutoConfirm(false);
            var bus = EventBus.Instance;
            if (bus != null)
            {
                if (_onSessionStarted != null) bus.onSessionStarted.RemoveListener(_onSessionStarted);
                if (_onSessionCompleted != null) bus.onSessionCompleted.RemoveListener(_onSessionCompleted);
                if (_onSessionEnded != null) bus.onSessionEnded.RemoveListener(_onSessionEnded);
                if (_onGroupStarted != null) bus.onGroupStarted.RemoveListener(_onGroupStarted);
                if (_onGroupCompleted != null) bus.onGroupCompleted.RemoveListener(_onGroupCompleted);
                if (_onTaskStarted != null) bus.onTaskStarted.RemoveListener(_onTaskStarted);
                if (_onTaskCompleted != null) bus.onTaskCompleted.RemoveListener(_onTaskCompleted);
                if (_onPpeStateChanged != null) bus.onPpeStateChanged.RemoveListener(_onPpeStateChanged);
                if (_onActionAttempt != null) bus.onActionAttempt.RemoveListener(_onActionAttempt);
                if (_onScoreChanged != null) bus.onScoreChanged.RemoveListener(_onScoreChanged);
                if (_onSafetyViolation != null) bus.onSafetyViolation.RemoveListener(_onSafetyViolation);
                if (_onCriticalFailure != null) bus.onCriticalSafetyFailure.RemoveListener(_onCriticalFailure);
                if (_onSafetyError != null) bus.onSafetyError.RemoveListener(_onSafetyError);
            }
            ConsequenceEvents.OnConsequenceStarted -= OnConsequenceStarted;
            ConsequenceEvents.OnConsequenceEnded -= OnConsequenceEnded;
        }

        private void OnConsequenceStarted(ConsequenceStartedEventArgs args)
        {
            string text = "ConsequenceStarted: " + args.MappingId;
            _result.consequences.Add(text);
            AddTranscript(text);
        }

        private void OnConsequenceEnded()
        {
            AddTranscript("ConsequenceEnded");
        }

        private void AddTranscript(string entry)
        {
            _result.transcript.Add(entry);
            RefreshSnapshot();
        }

        private void RefreshSnapshot()
        {
            _result.activeGroup = _taskManager?.GetCurrentGroup()?.id ?? string.Empty;
            _result.score = ScoreService.Instance.CurrentScore;
            _result.tasks.Clear();
            if (_taskManager == null) return;
            foreach (var task in _taskManager.GetSessionTasks())
            {
                string groupId = string.Empty;
                foreach (var group in _taskManager.RuntimeGroups)
                {
                    if (group == null || group.tasks == null) continue;
                    foreach (var groupTask in group.tasks)
                    {
                        if (ReferenceEquals(groupTask, task.TaskData))
                        {
                            groupId = group.id;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(groupId)) break;
                }
                _result.tasks.Add(new SimulationTaskSnapshot
                {
                    id = task.id,
                    name = task.taskName,
                    groupId = groupId,
                    state = task.State.ToString()
                });
            }
            _result.sessionSummary = _taskManager.LastSessionSummary;
        }

        private void ResetResult()
        {
            Unsubscribe();
            _prepared = false;
            _sessionStarted = false;
            _sessionCompleted = false;
            _inputObserved = false;
            _nextStep = 0;
            _result.status = SimulationStatus.Idle;
            _result.currentStep = -1;
            _result.activeGroup = string.Empty;
            _result.participantId = string.Empty;
            _result.lastDiagnostic = string.Empty;
            _result.score = 0;
            _result.sessionSummary = null;
            _result.tasks.Clear();
            _result.transcript.Clear();
            _result.consequences.Clear();
        }

        private void Fail(string diagnostic)
        {
            _result.status = SimulationStatus.Failed;
            _result.lastDiagnostic = diagnostic;
            SafetyLog.Error("[SessionSimulator] " + diagnostic, this);
            RefreshSnapshot();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            _busy = false;
            Unsubscribe();
            if (_instance == this) _instance = null;
        }
    }
}
#endif
