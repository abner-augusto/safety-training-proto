using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Runtime.Safety
{
    /// <summary>
    /// Evaluation-only escape hatch for phase 1: lets the participant advance
    /// to the scaffold with PPE tasks still open. On press it (1) applies ONE
    /// order penalty if the equip order deviated from the authored sequence,
    /// (2) closes every open task as Omitted (TASK_OMITTED, 0 pts — see
    /// TaskManagerCore.MarkPendingTasksOmitted), which completes the group and
    /// triggers the normal PhaseController transition. Deliberately silent:
    /// no warning, no list of what is missing — the finish screen carries all
    /// feedback, otherwise the gate would leak the answers it exists to hide.
    /// In Guided mode the button deactivates itself (the group auto-completes).
    /// </summary>
    public class PhaseAdvanceGate : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TaskManager taskManager;
        [Tooltip("Objects to hide when the gate is disarmed (Guided mode) or consumed (after a successful advance) — the button visual/canvas.")]
        [SerializeField] private GameObject[] gateButtonObjects;

        [Header("Gate Configuration")]
        [Tooltip("Group id this gate advances past. Compared against TaskGroupDef.id from the scenario JSON.")]
        [SerializeField] private string targetGroupId = "ppe_selection";

        private UnityAction<SessionStartedEventArgs>? _onSessionStarted;
        private bool _consumed;

        private void Start()
        {
            // Keep the gate hidden until a live session explicitly enters Evaluation.
            SetButtonsActive(false);

            if (taskManager == null)
                taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindFirstObjectByType<TaskManager>();

            if (taskManager == null)
            {
                SafetyLog.Error("[PhaseAdvanceGate] TaskManager not found.", this);
                enabled = false;
                return;
            }

            _onSessionStarted = OnSessionStarted;

            var eventBus = EventBus.Instance;
            if (eventBus == null)
            {
                SafetyLog.Error("[PhaseAdvanceGate] EventBus not found.", this);
                enabled = false;
                return;
            }

            eventBus.onSessionStarted.AddListener(_onSessionStarted);
        }

        private void OnDestroy()
        {
            var eventBus = EventBus.Instance;
            if (eventBus != null && _onSessionStarted != null)
                eventBus.onSessionStarted.RemoveListener(_onSessionStarted);
        }

        private void OnSessionStarted(SessionStartedEventArgs _)
        {
            _consumed = false;
            SetButtonsActive(SessionModeState.Current == SessionMode.Evaluation);
        }

        /// <summary>Wire to the advance button's OnClick (DualModeButton).</summary>
        public void Advance()
        {
            if (_consumed) return;
            if (SessionModeState.Current != SessionMode.Evaluation) return;

            var currentGroup = taskManager.GetCurrentGroup();
            if (currentGroup == null || !string.Equals(currentGroup.id, targetGroupId, System.StringComparison.Ordinal))
            {
                SafetyLog.Info($"[PhaseAdvanceGate] Ignorado — grupo atual não é '{targetGroupId}'.", this);
                return;
            }

            _consumed = true;

            ApplyOrderPenaltyIfDeviated(currentGroup.id, currentGroup.groupName);

            var omitted = taskManager.MarkPendingTasksOmitted();
            SafetyLog.Info($"[PhaseAdvanceGate] Avanço com {omitted.Count} tarefa(s) omitida(s).", this);

            // Group completion (raised by MarkPendingTasksOmitted, or already
            // complete) drives PhaseController's confirm + teleport. The button
            // is consumed either way.
            SetButtonsActive(false);
        }

        private void ApplyOrderPenaltyIfDeviated(string groupId, string groupName)
        {
            var deviations = taskManager.GetCompletionOrderDeviations();
            if (deviations.Count == 0) return;

            string list = string.Join(", ", deviations);

            taskManager.RegisterOrderViolation($"EPIs fora da ordem recomendada: {list}");
            SafetyEvents.RaiseSafetyViolation(new SafetyViolationEventArgs
            {
                ViolationCode = "ORDER_VIOLATION",
                Message = $"EPIs equipados fora da ordem recomendada: {list}",
                TaskId = string.Empty,
                GroupId = groupId,
                TaskName = string.Empty,
                GroupName = groupName
            });

            var scoring = taskManager.Scoring ?? ScoringConfig.Default;
            int charge = scoring.BasePenaltyFor(TaskSeverity.Minor);
            if (charge > 0)
                ScoreService.Instance.SubtractPoints(charge, "ORDER_VIOLATION", string.Empty);
        }

        private void SetButtonsActive(bool active)
        {
            if (gateButtonObjects == null) return;
            foreach (var go in gateButtonObjects)
                if (go != null) go.SetActive(active);
        }
    }
}
