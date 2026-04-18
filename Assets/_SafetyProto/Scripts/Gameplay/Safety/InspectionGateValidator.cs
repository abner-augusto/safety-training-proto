using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.Task;
using SafetyProto.UI;
using UnityEngine;

namespace SafetyProto.Gameplay.Safety
{
    public enum ConsequenceType
    {
        /// <summary>Activates a Rigidbody and applies lateral force (missing guardrail/toeboard).</summary>
        ObjectFall,

        /// <summary>Vignette + screen fade via OVRScreenFade (disconnected harness).</summary>
        PlayerFallSimulation,

        /// <summary>World-space alert panel near the problematic object.</summary>
        VisualAlert,
    }

    [Serializable]
    public class ConsequenceMapping
    {
        [Tooltip("ActionId of the task (e.g. 'connect_harness', 'install_guardrail').")]
        public string taskActionId;

        public string displayName;
        public ConsequenceType consequenceType;

        [Tooltip("GameObject that plays the consequence animation. Can be null for camera-based effects.")]
        public GameObject consequenceTarget;

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

        [Header("Gate Configuration")]
        [SerializeField] private int penaltyPerAttempt = 100;

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

        // ── Private ───────────────────────────────────────────────

        private bool _isProcessing;
        private readonly List<string> _lastPendingTaskIds = new List<string>();

        // ──────────────────────────────────────────────────────────

        private void Start()
        {
            if (taskManager == null)
                taskManager = FindFirstObjectByType<TaskManager>();

            if (taskManager == null)
                SafetyLog.Error("[InspectionGateValidator] TaskManager not found.", this);

            FailedAttemptCount = 0;
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
            if (currentGroup.executionMode != TaskExecutionMode.FreeOrder)
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

            if (pendingTasks.Count == 0)
            {
                PlaySound(successSound);
                OnInspectionPassed();
                return;
            }

            // Failed attempt
            FailedAttemptCount++;
            _lastPendingTaskIds.Clear();
            _lastPendingTaskIds.AddRange(pendingTasks.Select(t => t.ExpectedActionId));

            ScoreService.Instance.SubtractPoints(penaltyPerAttempt, "GATE_PENALTY");

            StartCoroutine(ExecuteConsequencesSequence(pendingTasks, currentGroup));
        }

        // ── Passed ────────────────────────────────────────────────

        private void OnInspectionPassed()
        {
            if (verboseLogging)
                SafetyLog.Info("[InspectionGateValidator] Inspection passed. Advancing.", this);

            // The TaskManager advances automatically when the group is complete.
            // No additional call needed — gate just unblocks.
        }

        // ── Consequence sequence ──────────────────────────────────

        private IEnumerator ExecuteConsequencesSequence(
            List<RuntimeSafetyTask> pendingTasks,
            TaskGroup currentGroup)
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
                // Emit safety violation for SessionLogger
                SafetyEvents.RaiseSafetyViolation(new SafetyViolationEventArgs
                {
                    ViolationCode = mapping != null ? "GATE_FAILED" : "INSPECTION_INCOMPLETE",
                    Message = $"Tentou iniciar sem corrigir: {mapping?.displayName ?? task.taskName}",
                    TaskId = task.taskName,
                    GroupId = currentGroup.groupName
                });

                if (mapping == null)
                {
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

                switch (mapping.consequenceType)
                {
                    case ConsequenceType.ObjectFall:
                        yield return ExecuteObjectFall(mapping);
                        break;

                    case ConsequenceType.PlayerFallSimulation:
                        yield return ExecutePlayerFallSimulation(mapping);
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
                ShowConsequenceFeedback(mapping.displayName, mapping.feedbackMessage);
                ConsequenceEvents.RaiseConsequenceEnded();

                yield return new WaitForSeconds(delayBetweenConsequences);
            }

            yield return new WaitForSeconds(delayAfterAllConsequences);
            HideConsequenceFeedback();
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

        private static readonly WaitForSeconds _waitFadeOut   = new(0.8f);
        private static readonly WaitForSeconds _waitHoldBlack = new(1.0f);
        private static readonly WaitForSeconds _waitFadeIn    = new(0.5f);
        private static readonly WaitForSeconds _waitFallTotal = new(2.6f);

        private IEnumerator ExecutePlayerFallSimulation(ConsequenceMapping mapping)
        {
            // 1. Fade out — duration is set via OVRScreenFade.fadeTime property
            if (OVRScreenFade.instance != null)
            {
                float prevFadeTime = OVRScreenFade.instance.fadeTime;
                OVRScreenFade.instance.fadeTime = 0.8f;
                OVRScreenFade.instance.FadeOut();
                yield return _waitFadeOut;

                // 2. Hold black
                yield return _waitHoldBlack;

                // 3. Fade back in
                OVRScreenFade.instance.fadeTime = 0.5f;
                OVRScreenFade.instance.FadeIn();
                yield return _waitFadeIn;

                OVRScreenFade.instance.fadeTime = prevFadeTime;
            }
            else
            {
                // Fallback if OVRScreenFade is not present
                yield return _waitFallTotal;
            }
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
            if (PopupService.Instance == null)
            {
                SafetyLog.Warning("[InspectionGateValidator] PopupService not found — consequence feedback skipped.", this);
                return;
            }

            PopupService.Instance.ShowWarning(title, message);
        }

        private void HideConsequenceFeedback()
        {
            if (PopupService.Instance == null)
            {
                SafetyLog.Warning("[InspectionGateValidator] PopupService not found — cannot hide feedback.", this);
                return;
            }

            PopupService.Instance.Hide();
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }
    }
}
