using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scoring;
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
    /// Gate placed on the "Iniciar Atividade" button.
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

        // ── Public state (read by SessionReportUI) ────────────────

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

        public bool IsSimulationProcessing => _isProcessing;

        // ── Private ───────────────────────────────────────────────

        private bool _isProcessing;
        private bool _showSessionEndPanels;
        private bool _simulationAutoConfirm;
        private bool _simulationCancellationRequested;
        private float _playerFallPreviousFadeTime;
        private bool _playerFallFadeActive;
        private readonly List<string> _lastPendingTaskIds = new List<string>();

        // Tasks already charged at a failed gate press this session. The charge
        // attaches to the task, not the press: pressing again with the same pending
        // set costs nothing more (but is still logged), so a disoriented participant
        // is not taxed repeatedly for one mistake.
        private readonly HashSet<string> _chargedTaskIds = new HashSet<string>();

        private ScoringConfig GateScoring =>
            taskManager != null ? taskManager.Scoring : ScoringConfig.Default;

        // ──────────────────────────────────────────────────────────

        private void Start()
        {
            if (taskManager == null)
                taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindFirstObjectByType<TaskManager>();

            if (taskManager == null)
                SafetyLog.Error("[InspectionGateValidator] TaskManager not found.", this);

            _popupFeedback = popupFeedbackProvider as IPopupFeedback;
            EventBus.Instance?.onSessionCompleted.AddListener(OnSessionCompletedEvent);

            FailedAttemptCount = 0;
            _chargedTaskIds.Clear();
            HideConsequenceFeedback();

        }

        /// <summary>
        /// Call this from RayInteractable.WhenSelect on the "Iniciar Atividade" button.
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

            // Guard: only operates on FreeOrder groups
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
                    _popupFeedback.ShowConfirmation(
                        "Iniciar Atividade",
                        "Deseja iniciar a atividade?",
                        "Iniciar",
                        "Voltar",
                        onConfirm: () => BeginEvaluationFinish(pendingTasks),
                        onCancel: () => { _isProcessing = false; });
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

            // Failed attempt — skip consequences in Guided mode
            FailedAttemptCount++;
            _lastPendingTaskIds.Clear();
            _lastPendingTaskIds.AddRange(pendingTasks.Select(t => t.ExpectedActionId));

            foreach (var task in pendingTasks)
            {
                if (task.TaskData == null || !_chargedTaskIds.Add(task.id)) continue;
                int charge = GateScoring.GateChargeFor(task.TaskData.riskLevel);
                if (charge > 0)
                    ScoreService.Instance.SubtractPoints(charge, "GATE_PENALTY", task.id);
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
                // Repeat press: violations still logged, no animation replay.
                foreach (var task in pendingTasks)
                {
                    SafetyEvents.RaiseSafetyViolation(new SafetyViolationEventArgs
                    {
                        ViolationCode = "GATE_FAILED",
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

        // ── Passed ────────────────────────────────────────────────

        // B7: all tasks complete → success popup with a manual "Continuar" button that ends the
        // session (and shows the finish screen). No timed auto-dismiss.
        private void ShowSuccessAndEnd(ITaskGroup currentGroup)
        {
            if (verboseLogging)
                SafetyLog.Info("[InspectionGateValidator] Inspeção aprovada. Aguardando 'Continuar' para finalizar a sessão.", this);

            // Inspection passed: take the gate button (and its canvas) out so it can't be pressed again.
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

        // ── Consequence sequence ──────────────────────────────────

        private IEnumerator ExecuteConsequencesSequence(
            List<RuntimeSafetyTask> pendingTasks,
            ITaskGroup currentGroup,
            bool includeFallback,
            bool emitViolations,
            Action onComplete)
        {
            _isProcessing = true;

            // Build ordered mapping list — PlayerFallSimulation always last
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

            foreach (var (task, mapping) in pendingMappings)
            {
                if (_simulationCancellationRequested) yield break;
                if (emitViolations)
                {
                    SafetyEvents.RaiseSafetyViolation(new SafetyViolationEventArgs
                    {
                        ViolationCode = mapping != null ? "GATE_FAILED" : "INSPECTION_INCOMPLETE",
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
                    // Fallback: generic warning + hintText
                    PlaySound(warningSound);
                    ShowConsequenceFeedback(task.taskName, task.TaskData?.hintText ?? task.taskName);
                    yield return new WaitForSeconds(delayBetweenConsequences);
                    continue;
                }

                ConsequenceEvents.RaiseConsequenceStarted(new ConsequenceStartedEventArgs
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
                        // A3: blackout-only mock uses the fade-only path; otherwise keep the controlled fall.
                        if (mapping.blackoutOnly)
                        {
                            yield return ExecutePlayerFallSimulation(mapping);
                            feedbackHandledByConsequence = true;
                        }
                        else if (fallController != null)
                            yield return fallController.TriggerControlledFall();
                        else
                        {
                            yield return ExecutePlayerFallSimulation(mapping);
                            feedbackHandledByConsequence = true;
                        }
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
                ConsequenceEvents.RaiseConsequenceEnded();

                yield return new WaitForSeconds(delayBetweenConsequences);
            }

            yield return new WaitForSeconds(delayAfterAllConsequences);
            HideConsequenceFeedback();

            // B7: warn-and-continue. The gate no longer ends the session on failure — list the
            // still-pending tasks and let the player keep going to finish them. _isProcessing is
            // released only when the player presses "Continuar".
            if (onComplete != null)
                onComplete();
            else
                ShowPendingWarningAndContinue(pendingTasks);
        }

        // B7: warning popup listing the remaining tasks, with a "Continuar" button that dismisses
        // and lets the player keep playing (no SessionCompleted).
        private void ShowPendingWarningAndContinue(List<RuntimeSafetyTask> pendingTasks)
        {
            string list = string.Join("\n", pendingTasks.Select(t =>
                "• " + (string.IsNullOrWhiteSpace(t.taskName) ? t.ExpectedActionId : t.taskName)));
            string body = $"Você ainda não concluiu todas as tarefas de segurança:\n{list}\n\n" +
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

        // ── Evaluation finish ────────────────────────────────────

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

        // ── Individual consequence implementations ────────────────

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

        private IEnumerator ExecutePlayerFallSimulation(ConsequenceMapping mapping)
        {
            // Keep the world black while the consequence explanation is visible. The popup is an
            // overlay, so the participant can read it without seeing the simulated fall setup.
            bool continued = _simulationAutoConfirm || _simulationCancellationRequested;

            if (OVRScreenFade.instance != null)
            {
                _playerFallPreviousFadeTime = OVRScreenFade.instance.fadeTime;
                _playerFallFadeActive = true;
                OVRScreenFade.instance.fadeTime = 0.8f;
                OVRScreenFade.instance.FadeOut();
                yield return new WaitForSeconds(0.8f);

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

        // ── Helpers ───────────────────────────────────────────────

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
                EventBus.Instance.onSessionCompleted.RemoveListener(OnSessionCompletedEvent);
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
            foreach (var panel in sessionEndPanels)
                if (panel != null) panel.SetActive(true);
        }
    }
}
