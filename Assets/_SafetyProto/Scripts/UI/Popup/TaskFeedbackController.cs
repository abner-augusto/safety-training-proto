using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Feedback;
using SafetyProto.Domain.Safety;
using SafetyProto.Runtime.PPE;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.UI
{
    public class TaskFeedbackController : MonoBehaviour
    {
        [Header("PPE Snap Slots — drag in every slot of the body rig")]
        [SerializeField] private PPESnapSlot[] snapSlots;

        [Header("Popup Titles")]
        [SerializeField] private string ppeTitle = "EPI Incorreto";
        [SerializeField] private string wrongOrderTitle = "Ordem Incorreta";
        [Tooltip("Alert title when the task is refused because the group prerequisite (e.g. lanyard anchored) is still pending.")]
        [SerializeField] private string prerequisiteTitle = "Conecte-se Primeiro";

        [Header("Reading Time")]
        [Tooltip("Minimum reading time (s) for the task/PPE alerts, with the session clock held. " +
                 "Afterwards the alert stays up until the participant's next action.")]
        [SerializeField] private float autoCloseSeconds = 6f;

        private TaskManager _taskManager;

        private void Start()
        {
            _taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindAnyObjectByType<TaskManager>();
            if (_taskManager == null)
                SafetyLog.Warning("[TaskFeedbackController] TaskManager not found.", this);

            if (!this.IsEventBusReady()) return;

            EventBus.Instance.Subscribe<TaskEventArgs>(OnTaskCompleted);
            EventBus.Instance.Subscribe<SafetyViolationEventArgs>(OnSafetyViolation);

            foreach (var slot in snapSlots)
                if (slot != null)
                {
                    slot.onDistractorSnapAttempted.AddListener(OnDistractorSnapAttempted);
                    slot.onWrongOrderSnapAttempted.AddListener(OnWrongOrderSnapAttempted);
                }
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.Unsubscribe<TaskEventArgs>(OnTaskCompleted);
                EventBus.Instance.Unsubscribe<SafetyViolationEventArgs>(OnSafetyViolation);
            }

            foreach (var slot in snapSlots)
                if (slot != null)
                {
                    slot.onDistractorSnapAttempted.RemoveListener(OnDistractorSnapAttempted);
                    slot.onWrongOrderSnapAttempted.RemoveListener(OnWrongOrderSnapAttempted);
                }
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.Phase != TaskPhase.Completed) return;
            if (args.RuntimeTask == null) return;

            if (args.RuntimeTask.State == TaskState.CompletedSuccessButUnsafe)
            {
                var ppeText = args.Task?.ppeAdvice;
                if (!string.IsNullOrWhiteSpace(ppeText))
                    PopupService.Instance?.ShowFeedbackWarning(ppeTitle, ppeText, autoCloseSeconds,
                        narrationKey: NarrationKeys.ForPpeAdvice(args.Task?.id));
            }
        }

        /// <summary>
        /// A task was refused because the group's safety precondition is still pending (the
        /// participant tried to work before anchoring the lanyard). The engine already carries
        /// the authored pt-BR explanation in the violation message, so this only frames it.
        /// Every other violation code has its own surface (LogHUD, dashboard) and is ignored here.
        /// </summary>
        private void OnSafetyViolation(SafetyViolationEventArgs args)
        {
            if (args.ViolationCode != ViolationCodes.PrerequisitePending) return;
            if (string.IsNullOrWhiteSpace(args.Message)) return;

            PopupService.Instance?.ShowFeedbackWarning(prerequisiteTitle, args.Message, autoCloseSeconds,
                ViolationCodes.PrerequisitePending, NarrationKeys.PrerequisitePending);
        }

        private void OnDistractorSnapAttempted(PPEType attempted, string distractorId)
        {
            var match = DistractorResolver.Resolve(_taskManager != null ? _taskManager.RuntimeGroups : null, distractorId);

            string body;
            if (match != null)
            {
                body = DistractorResolver.BuildRejectionBody(match);
            }
            else
            {
                SafetyLog.Warning(
                    $"[TaskFeedbackController] No distractor with id '{distractorId}' is authored in the scenario ({attempted}); showing the generic rejection.",
                    this);
                body = "Este equipamento não é adequado para trabalho em altura.";
            }

            // Narrated per decoy (not per text): only an authored decoy has a voice-over.
            var narrationKey = match != null ? NarrationKeys.ForDistractor(match.Distractor.Id) : null;
            PopupService.Instance?.ShowFeedbackWarning(ppeTitle, body, autoCloseSeconds, narrationKey: narrationKey);
        }

        private void OnWrongOrderSnapAttempted(PPEType attempted)
        {
            var hint = _taskManager?.CurrentRuntimeTask?.TaskData?.hintText;

            var body = !string.IsNullOrWhiteSpace(hint)
                ? $"Este equipamento ainda não é o próximo da sequência.\n\nDica: {hint}"
                : "Este equipamento ainda não é o próximo da sequência. Siga a ordem correta das tarefas.";

            PopupService.Instance?.ShowFeedbackWarning(wrongOrderTitle, body, autoCloseSeconds,
                narrationKey: NarrationKeys.WrongOrder);
        }
    }
}
