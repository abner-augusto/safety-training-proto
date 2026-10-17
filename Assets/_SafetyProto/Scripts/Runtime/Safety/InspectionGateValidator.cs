using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Sessions;
using SafetyProto.Runtime.Task;
using RuntimeSafetyTask = SafetyProto.Core.RuntimeSafetyTask;
using ConsequenceType = SafetyProto.Core.Events.ConsequenceType;
using UnityEngine;

namespace SafetyProto.Runtime.Safety
{
    [Serializable]
    public class ConsequenceMapping
    {
        [Tooltip("ActionId of the task (e.g. 'connect_harness', 'install_guardrail').")]
        public string taskActionId;

        public string displayName;
        public ConsequenceType consequenceType;

        [Tooltip("GameObject that plays the consequence animation. Can be null for camera-based effects.")]
        public GameObject consequenceTarget;

        [Tooltip("When true, PlayerFallSimulation uses the blackout-only fade path and skips the controlled fall rig.")]
        public bool blackoutOnly;

        [TextArea(2, 4)]
        public string feedbackMessage;

        [Tooltip("Fallback: uses warningSound if null.")]
        public AudioClip consequenceSound;
    }

    /// <summary>
    /// Gate placed on the Finalizar Inspeção e Iniciar Atividade button.
    /// When triggered, checks if all tasks in the current FreeOrder group are complete.
    /// If not, executes visual/physical consequences for each pending task and penalizes the score.
    /// </summary>
    public class InspectionGateValidator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TaskManager taskManager;
        [SerializeField] private MonoBehaviour popupFeedbackProvider;
        [SerializeField] private TimerSystem timerSystem;
        [Tooltip("Panels to activate after SessionCompleted fires (e.g. the session report canvas). Activated after the event so their OnEnable can read the cached args.")]
        [SerializeField] private GameObject[] sessionEndPanels;
        [Tooltip("GameObjects to deactivate when the inspection passes (e.g. the 'Iniciar Atividade' button or the canvas it lives on). Left active while any task is still pending.")]
        [SerializeField] private GameObject[] gateButtonObjects;
        [Tooltip("Optional (A3). If set, the PlayerFallSimulation consequence routes through its controlled fall and is skipped when the player is correctly anchored.")]
        [SerializeField] private FallFromHeightController fallController;

        private IPopupFeedback _popupFeedback;

        [Header("Gate Configuration")]
        [Tooltip("Temporarily hide gateButtonObjects while the evaluation confirmation is open. Cancel restores only objects that were active.")]
        [SerializeField] private bool hideGateButtonsWhileConfirming = true;

        [Tooltip("Label for the manual-dismiss button on the success / warning popups.")]
        [SerializeField] private string continueButtonLabel = "Continuar";

        [Header("Consequence Timing")]
        [SerializeField] private float delayBetweenConsequences = 2.5f;
        [SerializeField] private float delayAfterAllConsequences = 1.5f;

        [Header("Object Fall Settings")]
        [SerializeField] private float fallForceMultiplier = 5f;

        [Header("Consequence Definitions")]
        [SerializeField] private List<ConsequenceMapping> consequenceMappings;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip warningSound;
        [SerializeField] private AudioClip successSound;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        /// <summary>How many times the user tried to start without completing the inspection.</summary>
        public int FailedAttemptCount { get; private set; }

        /// <summary>ActionIds that were pending on the last failed attempt.</summary>
        public IReadOnlyList<string> LastPendingTaskIds => _lastPendingTaskIds.AsReadOnly();

        /// <summary>Used only by the Editor-only simulator to dismiss confirmation UI.</summary>
        public void SetSimulationAutoConfirm(bool enabled)
        {
            _simulationAutoConfirm = enabled;
            if (enabled) _simulationCancellationRequested = false;
        }

        /// <summary>Stops only an inspection sequence explicitly started for the simulator.</summary>
        public void CancelSimulationProcessing()
        {
            if (!_simulationAutoConfirm) return;

            _simulationCancellationRequested = true;
            RestorePlayerFallFadeAfterCancellation();
            StopAllCoroutines();
            HideConsequenceFeedback();
            _isProcessing = false;
            _showSessionEndPanels = false;
            _simulationAutoConfirm = false;
        }

        /// <summary>
        /// Raises the wired session-end panels for a run that reached SessionCompleted without
        /// passing through this gate. Only the gate holds the panel references, so a simulated
        /// session has no other way to show the report.
        /// </summary>
        public void ShowSessionEndPanelsNow()
        {
            _showSessionEndPanels = true;
            ActivateSessionEndPanelsIfComplete();
        }

        public bool IsSimulationProcessing => _isProcessing;

        private bool _isProcessing;
        private bool _showSessionEndPanels;
        private bool _simulationAutoConfirm;
        private bool _simulationCancellationRequested;
        private float _playerFallPreviousFadeTime;
        private bool _playerFallFadeActive;
        private readonly List<string> _lastPendingTaskIds = new List<string>();

        private readonly HashSet<string> _chargedTaskIds = new HashSet<string>();

        private ScoringConfig GateScoring =>
            taskManager != null ? taskManager.Scoring : ScoringConfig.Default;

        private void Start()
        {
            if (taskManager == null)
                taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindFirstObjectByType<TaskManager>();

            if (taskManager == null)
                SafetyLog.Error("[InspectionGateValidator] TaskManager not found.", this);

            _popupFeedback = popupFeedbackProvider as IPopupFeedback;
            EventBus.Instance?.Subscribe<SessionCompletedEventArgs>(OnSessionCompletedEvent);

            FailedAttemptCount = 0;
            _chargedTaskIds.Clear();
            HideConsequenceFeedback();

        }

        /// <summary>
        /// Call this from RayInteractable.WhenSelect on the Finalizar Inspeção e Iniciar Atividade button.
        /// </summary>
        public void Validate()
        {
            if (_isProcessing) return;

            if (taskManager == null)
            {
                SafetyLog.Error("[InspectionGateValidator] Validate() called but TaskManager is null.", this);
                return;
            }

            var currentGroup = taskManager.GetCurrentGroup();
            if (currentGroup == null)
            {
                SafetyLog.Warning("[InspectionGateValidator] No active group.", this);
                return;
            }

            if (currentGroup.executionMode != TaskExecutionModeShared.FreeOrder)
            {
                if (verboseLogging)
                    SafetyLog.Info("[InspectionGateValidator] Current group is Sequential — gate skipped.", this);
                return;
            }

            var sessionTasks = taskManager.GetSessionTasks();
            var pendingTasks = sessionTasks
                .Where(t => currentGroup.tasks.Any(x => ReferenceEquals(x, t.TaskData)))
                .Where(t => t.State == TaskState.NotStarted || t.State == TaskState.InProgress)
                .ToList();

            if (SessionModeState.Current == SessionMode.Evaluation)
            {
                _isProcessing = true;

                if (_simulationAutoConfirm)
                {
                    BeginEvaluationFinish(pendingTasks);
                }
                else if (_popupFeedback != null)
                {
                    var hiddenButtons = hideGateButtonsWhileConfirming && gateButtonObjects != null
                        ? gateButtonObjects.Where(go => go != null && go.activeSelf).ToArray()
                        : Array.Empty<GameObject>();
                    foreach (var go in hiddenButtons)
                        go.SetActive(false);

                    StartCoroutine(ShowFinishConfirmationNextFrame(pendingTasks, hiddenButtons));
                }
                else
                {
                    BeginEvaluationFinish(pendingTasks);
                }

                return;
            }

            if (pendingTasks.Count == 0)
            {
                _isProcessing = true;
                PlaySound(successSound);
                ShowSuccessAndEnd(currentGroup);
                return;
            }

            FailedAttemptCount++;
            _lastPendingTaskIds.Clear();
            _lastPendingTaskIds.AddRange(pendingTasks.Select(t => t.ExpectedActionId));

            foreach (var task in pendingTasks)
            {
                if (task.TaskData == null || !_chargedTaskIds.Add(task.id)) continue;
                IncidentalChargePolicy.ChargeGatePenalty(ScoreService.Instance, GateScoring, task.TaskData);
            }

            if (SessionModeState.Current == SessionMode.Guided)
            {
                _isProcessing = true;
                ShowPendingWarningAndContinue(pendingTasks);
                return;
            }

            if (FailedAttemptCount == 1)
            {
                StartCoroutine(ExecuteConsequencesSequence(pendingTasks, currentGroup, includeFallback: true, emitViolations: true, onComplete: null));
            }
            else
            {
                foreach (var task in pendingTasks)
                {
                    SafetyEvents.RaiseSafetyViolation(new SafetyViolationEventArgs
                    {
                        ViolationCode = ViolationCodes.GateFailed,
                        Message = $"Tentou iniciar sem corrigir: {task.taskName}",
                        TaskId = task.id,
                        GroupId = currentGroup.id,
                        TaskName = task.taskName,
                        GroupName = currentGroup.groupName
                    });
                }
                _isProcessing = true;
                ShowPendingWarningAndContinue(pendingTasks);
            }
        }

        private void ShowSuccessAndEnd(ITaskGroup currentGroup)
        {
            if (verboseLogging)
                SafetyLog.Info("[InspectionGateValidator] Inspeção aprovada. Aguardando 'Continuar' para finalizar a sessão.", this);

            HideGateButtons();

            void Finish()
            {
                HideConsequenceFeedback();
                _showSessionEndPanels = true;
                taskManager.CloseCurrentGroup();
                ActivateSessionEndPanelsIfComplete();
                _isProcessing = false;
            }

            if (_simulationAutoConfirm)
                Finish();
            else if (_popupFeedback != null)
                _popupFeedback.ShowInteractive(currentGroup.groupName,
                    "Inspeção concluída com sucesso!", continueButtonLabel, Finish);
            else
                Finish();
        }

        // OVROverlayCanvas layer setup/teardown is flushed at end of frame: changing the gate and popup in one frame shows stale content.
        // Keep the two transitions one frame apart.
        private IEnumerator ShowFinishConfirmationNextFrame(List<RuntimeSafetyTask> pendingTasks, GameObject[] hiddenButtons)
        {
            yield return null;

            _popupFeedback.ShowConfirmation(
                "Finalizar Inspeção e Iniciar Atividade",
                "Deseja finalizar a inspeção e iniciar a atividade?",
                "Iniciar",
                "Voltar",
                onConfirm: () => BeginEvaluationFinish(pendingTasks),
                onCancel: () => StartCoroutine(RestoreGateButtonsNextFrame(hiddenButtons)));
        }

        private IEnumerator RestoreGateButtonsNextFrame(GameObject[] hiddenButtons)
        {
            yield return null;

            foreach (var go in hiddenButtons)
                if (go != null) go.SetActive(true);
            _isProcessing = false;
        }

        private IEnumerator ExecuteConsequencesSequence(
            List<RuntimeSafetyTask> pendingTasks,
            ITaskGroup currentGroup,
            bool includeFallback,
            bool emitViolations,
            Action onComplete)
        {
            _isProcessing = true;

            var pendingMappings = new List<(RuntimeSafetyTask task, ConsequenceMapping mapping)>();
            foreach (var task in pendingTasks)
            {
                var mapping = consequenceMappings?
                    .FirstOrDefault(m => string.Equals(m.taskActionId, task.ExpectedActionId,
                        StringComparison.OrdinalIgnoreCase));
                pendingMappings.Add((task, mapping));
            }

            pendingMappings.Sort((a, b) =>
            {
                if (a.mapping?.consequenceType == ConsequenceType.PlayerFallSimulation) return 1;
                if (b.mapping?.consequenceType == ConsequenceType.PlayerFallSimulation) return -1;
                return 0;
            });

            bool terminalConsequence = false;

            foreach (var (task, mapping) in pendingMappings)
            {
                if (_simulationCancellationRequested) yield break;
                if (emitViolations)
                {
                    SafetyEvents.RaiseSafetyViolation(new SafetyViolationEventArgs
                    {
                        ViolationCode = mapping != null ? ViolationCodes.GateFailed : ViolationCodes.InspectionIncomplete,
                        Message = $"Tentou iniciar sem corrigir: {mapping?.displayName ?? task.taskName}",
                        TaskId = task.id,
                        GroupId = currentGroup.id,
                        TaskName = task.taskName,
                        GroupName = currentGroup.groupName
                    });
                }

                if (mapping == null)
                {
                    if (!includeFallback) continue;
                    PlaySound(warningSound);
                    ShowConsequenceFeedback(task.taskName, task.TaskData?.hintText ?? task.taskName);
                    yield return new WaitForSeconds(delayBetweenConsequences);
                    continue;
                }

                DirectConsequenceChannel.RaiseConsequenceStarted(new ConsequenceStartedEventArgs
                {
                    ConsequenceType = mapping.consequenceType,
                    TargetObject = mapping.consequenceTarget,
                    MappingId = mapping.taskActionId
                });

                bool feedbackHandledByConsequence = false;
                switch (mapping.consequenceType)
                {
                    case ConsequenceType.ObjectFall:
                        yield return ExecuteObjectFall(mapping);
                        break;

                    case ConsequenceType.PlayerFallSimulation:
                        bool collapsePlayed = false;
                        if (!mapping.blackoutOnly && fallController != null)
                        {
                            yield return fallController.TriggerControlledFall();
                            collapsePlayed = true;
                        }

                        yield return ExecutePlayerFallSimulation(mapping,
                            alreadyBlack: collapsePlayed,
                            keepBlackAfterContinue: true);
                        feedbackHandledByConsequence = true;
                        terminalConsequence = true;

                        SafetyEvents.RaiseCriticalSafetyFailure(new CriticalSafetyFailureEventArgs
                        {
                            Reason = $"Trabalhou desconectado: {mapping.displayName}",
                            ViolationCount = 1,
                            WindowSeconds = 0f
                        });
                        break;

                    case ConsequenceType.VisualAlert:
                        yield return ExecuteVisualAlert(mapping);
                        break;
                }

                PlaySound(mapping.consequenceSound != null ? mapping.consequenceSound : warningSound);
                if (!feedbackHandledByConsequence)
                    ShowConsequenceFeedback(mapping.displayName, mapping.feedbackMessage);
                DirectConsequenceChannel.RaiseConsequenceEnded();

                if (!terminalConsequence)
                    yield return new WaitForSeconds(delayBetweenConsequences);
            }

            if (!terminalConsequence)
                yield return new WaitForSeconds(delayAfterAllConsequences);
            HideConsequenceFeedback();

            if (onComplete != null)
                onComplete();
            else
                ShowPendingWarningAndContinue(pendingTasks);
        }

        private void ShowPendingWarningAndContinue(List<RuntimeSafetyTask> pendingTasks)
        {
            string list = string.Join("\n", pendingTasks.Select(t =>
                "• " + (string.IsNullOrWhiteSpace(t.taskName) ? t.ExpectedActionId : t.taskName)));
            string body = $"Você ainda não concluiu a inspeção — faltam estas tarefas:\n{list}\n\n" +
                          "Conclua as tarefas restantes antes de iniciar a atividade.";

            void Continue()
            {
                HideConsequenceFeedback();
                _isProcessing = false;
            }

            if (_simulationAutoConfirm)
                Continue();
            else if (_popupFeedback != null)
                _popupFeedback.ShowInteractive("Tarefas Pendentes", body, continueButtonLabel, Continue);
            else
                Continue();
        }

        /// <summary>
        /// Evaluation-mode gate confirm: play consequences only for pending tasks
        /// that have a mapped consequence, then close every open task as not performed.
        /// CloseCurrentGroup completes the group, so TaskManagerCore.EndSession
        /// publishes the single SessionCompleted/SessionEnded pair; this path must
        /// not raise its own.
        /// </summary>
        private void BeginEvaluationFinish(List<RuntimeSafetyTask> pendingTasks)
        {
            if (_simulationCancellationRequested) return;
            HideGateButtons();

            if (pendingTasks.Count == 0)
            {
                PlaySound(successSound);
                FinalizeEvaluation();
                return;
            }

            var mappedPending = pendingTasks
                .Where(t => consequenceMappings != null && consequenceMappings.Any(m =>
                    string.Equals(m.taskActionId, t.ExpectedActionId, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (mappedPending.Count > 0)
                StartCoroutine(ExecuteConsequencesSequence(
                    mappedPending,
                    taskManager.GetCurrentGroup(),
                    includeFallback: false,
                    emitViolations: false,
                    onComplete: FinalizeEvaluation));
            else
                FinalizeEvaluation();
        }

        private void FinalizeEvaluation()
        {
            if (_simulationCancellationRequested) return;
            var notPerformed = taskManager.CloseCurrentGroup();

            if (verboseLogging)
                SafetyLog.Info($"[InspectionGateValidator] Sessão finalizada em modo Avaliação ({notPerformed.Count} tarefa(s) não realizada(s)).", this);

            _showSessionEndPanels = true;
            ActivateSessionEndPanelsIfComplete();
            _isProcessing = false;
        }

        private IEnumerator ExecuteObjectFall(ConsequenceMapping mapping)
        {
            if (mapping.consequenceTarget == null)
            {
                if (verboseLogging)
                    SafetyLog.Warning($"[InspectionGateValidator] ObjectFall: consequenceTarget is null for '{mapping.taskActionId}'.", this);
                yield break;
            }

            mapping.consequenceTarget.SetActive(true);
            var rb = mapping.consequenceTarget.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.AddForce(new Vector3(0.5f, 0f, 0.3f) * fallForceMultiplier, ForceMode.Impulse);
            }

            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>
        /// Fade to black (unless the collapse already got us there), show the consequence popup
        /// over the black, and wait for "Continuar". With keepBlackAfterContinue the screen stays
        /// black afterwards: the finish screen's canvas renders in front of the fade rect, so the
        /// participant reads their report without the collapsed scaffold behind it.
        /// </summary>
        private IEnumerator ExecutePlayerFallSimulation(ConsequenceMapping mapping,
            bool alreadyBlack, bool keepBlackAfterContinue)
        {
            bool continued = _simulationAutoConfirm || _simulationCancellationRequested;

            if (OVRScreenFade.instance != null)
            {
                _playerFallPreviousFadeTime = OVRScreenFade.instance.fadeTime;
                _playerFallFadeActive = true;
                if (!alreadyBlack)
                {
                    OVRScreenFade.instance.fadeTime = 0.8f;
                    OVRScreenFade.instance.FadeOut();
                    yield return new WaitForSeconds(0.8f);
                }

                if (!continued && _popupFeedback != null)
                {
                    string message = string.IsNullOrWhiteSpace(mapping.feedbackMessage)
                        ? "Você caiu porque iniciou a atividade sem conectar o talabarte ao ponto de ancoragem."
                        : mapping.feedbackMessage;
                    _popupFeedback.ShowInteractive(mapping.displayName, message, continueButtonLabel, () =>
                    {
                        _popupFeedback.Hide();
                        continued = true;
                    });
                }
                else if (!continued)
                {
                    SafetyLog.Warning("[InspectionGateValidator] Popup de queda indisponível; mantendo blackout por um intervalo seguro e retomando o fluxo.", this);
                    yield return new WaitForSeconds(1f);
                    continued = true;
                }

                while (!continued && !_simulationCancellationRequested)
                    yield return null;

                _popupFeedback?.Hide();

                if (_simulationCancellationRequested)
                {
                    RestorePlayerFallFadeAfterCancellation();
                    yield break;
                }

                if (keepBlackAfterContinue)
                {
                    _playerFallFadeActive = false;
                    yield break;
                }

                OVRScreenFade.instance.fadeTime = 0.5f;
                OVRScreenFade.instance.FadeIn();
                yield return new WaitForSeconds(0.5f);

                OVRScreenFade.instance.fadeTime = _playerFallPreviousFadeTime;
                _playerFallFadeActive = false;
            }
            else
            {
                if (!continued && _popupFeedback != null)
                {
                    string message = string.IsNullOrWhiteSpace(mapping.feedbackMessage)
                        ? "Você caiu porque iniciou a atividade sem conectar o talabarte ao ponto de ancoragem."
                        : mapping.feedbackMessage;
                    _popupFeedback.ShowInteractive(mapping.displayName, message, continueButtonLabel, () =>
                    {
                        _popupFeedback.Hide();
                        continued = true;
                    });
                }
                else if (!continued)
                {
                    SafetyLog.Warning("[InspectionGateValidator] Popup de queda indisponível e OVRScreenFade ausente; retomando o fluxo sem bloquear a sessão.", this);
                    continued = true;
                }

                while (!continued && !_simulationCancellationRequested)
                    yield return null;
            }
        }

        private void RestorePlayerFallFadeAfterCancellation()
        {
            if (!_playerFallFadeActive || OVRScreenFade.instance == null) return;

            OVRScreenFade.instance.fadeTime = 0.5f;
            OVRScreenFade.instance.FadeIn();
            OVRScreenFade.instance.fadeTime = _playerFallPreviousFadeTime;
            _playerFallFadeActive = false;
        }

        private IEnumerator ExecuteVisualAlert(ConsequenceMapping mapping)
        {
            if (mapping.consequenceTarget != null)
                mapping.consequenceTarget.SetActive(true);

            PlaySound(warningSound);
            yield return new WaitForSeconds(2.0f);

            if (mapping.consequenceTarget != null)
                mapping.consequenceTarget.SetActive(false);
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }

        private void ShowConsequenceFeedback(string title, string message)
        {
            if (_popupFeedback == null)
            {
                SafetyLog.Warning("[InspectionGateValidator] IPopupFeedback not assigned — consequence feedback skipped.", this);
                return;
            }

            _popupFeedback.ShowWarning(title, message);
        }

        private void HideConsequenceFeedback()
        {
            _popupFeedback?.Hide();
        }

        private void HideGateButtons()
        {
            if (gateButtonObjects == null) return;
            foreach (var go in gateButtonObjects)
                if (go != null) go.SetActive(false);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.Unsubscribe<SessionCompletedEventArgs>(OnSessionCompletedEvent);
            StopAllCoroutines();
        }

        private void OnSessionCompletedEvent(SessionCompletedEventArgs _)
        {
            ActivateSessionEndPanelsIfComplete();
        }

        private void ActivateSessionEndPanelsIfComplete()
        {
            if (!_showSessionEndPanels || taskManager == null || !taskManager.LastSessionSummary.HasValue)
                return;

            _showSessionEndPanels = false;
            if (sessionEndPanels == null) return;
            StartCoroutine(ActivateSessionEndPanelsWhenPopupGone());
        }

        /// <summary>
        /// The paths that end the session hide a popup (the consequence or success notice) in the
        /// same frame they raise the report. Showing the report then draws it over a popup that is
        /// still fading out, so wait for the popup to be gone.
        /// </summary>
        private IEnumerator ActivateSessionEndPanelsWhenPopupGone()
        {
            float waited = 0f;
            while (!ReportRevealPolicy.ShouldReveal(_popupFeedback != null && _popupFeedback.IsDisplayed, waited))
            {
                yield return null;
                waited += Time.unscaledDeltaTime;
            }

            foreach (var panel in sessionEndPanels)
                if (panel != null) panel.SetActive(true);
        }
    }
}
