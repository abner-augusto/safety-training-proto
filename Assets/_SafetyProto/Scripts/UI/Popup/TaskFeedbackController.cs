using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.PPE;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.UI
{
    public class TaskFeedbackController : MonoBehaviour
    {
        [Header("PPE Snap Slots — arrastar todos os slots do body rig")]
        [SerializeField] private PPESnapSlot[] snapSlots;

        [Header("Títulos dos popups")]
        [SerializeField] private string ppeTitle = "EPI Incorreto";
        [SerializeField] private string wrongOrderTitle = "Ordem Incorreta";
        [Tooltip("Título do alerta quando a tarefa é recusada porque o pré-requisito do grupo (ex.: talabarte ancorado) ainda está pendente.")]
        [SerializeField] private string prerequisiteTitle = "Conecte-se Primeiro";

        [Header("Auto-fechamento")]
        [Tooltip("Tempo (s) para auto-fechar os alertas de task/EPI (ordem incorreta, EPI errado, etc.). 0 = sem timeout.")]
        [SerializeField] private float autoCloseSeconds = 6f;

        private TaskManager _taskManager;

        private void Start()
        {
            _taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindFirstObjectByType<TaskManager>();
            if (_taskManager == null)
                SafetyLog.Warning("[TaskFeedbackController] TaskManager not found.", this);

            if (!this.IsEventBusReady()) return;

            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
            EventBus.Instance.onSafetyViolation.AddListener(OnSafetyViolation);

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
                EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
                EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
            }

            foreach (var slot in snapSlots)
                if (slot != null)
                {
                    slot.onDistractorSnapAttempted.RemoveListener(OnDistractorSnapAttempted);
                    slot.onWrongOrderSnapAttempted.RemoveListener(OnWrongOrderSnapAttempted);
                }
        }

        // Only a completion reaches here. A task the participant never carried out is closed
        // by a gate without a TaskCompleted event, and its advice belongs on the final report
        // (see SessionReportUI) — not as a popup fired while the gate is teleporting them.
        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.RuntimeTask == null) return;

            if (args.RuntimeTask.State == TaskState.CompletedSuccessButUnsafe)
            {
                var ppeText = args.Task?.ppeAdvice;
                if (!string.IsNullOrWhiteSpace(ppeText))
                    PopupService.Instance?.ShowWarning(ppeTitle, ppeText, autoCloseSeconds);
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
            if (args.ViolationCode != "PREREQUISITE_PENDING") return;
            if (string.IsNullOrWhiteSpace(args.Message)) return;

            PopupService.Instance?.ShowWarning(prerequisiteTitle, args.Message, autoCloseSeconds);
        }

        private void OnDistractorSnapAttempted(PPEType attempted)
        {
            var task = _taskManager?.CurrentRuntimeTask?.TaskData;

            var advice = task?.ppeAdvice;
            var body = !string.IsNullOrWhiteSpace(advice)
                ? advice
                : "Este equipamento não é adequado para trabalho em altura.";

            // Attach the current task hint to reinforce the correct PPE.
            var hint = task?.hintText;
            if (!string.IsNullOrWhiteSpace(hint))
                body += $"\n\nDica: {hint}";

            PopupService.Instance?.ShowWarning(ppeTitle, body, autoCloseSeconds);
        }

        private void OnWrongOrderSnapAttempted(PPEType attempted)
        {
            // The item is valid PPE but was equipped before its turn. Point the player
            // at the task that is actually expected now via its hint.
            var hint = _taskManager?.CurrentRuntimeTask?.TaskData?.hintText;

            var body = !string.IsNullOrWhiteSpace(hint)
                ? $"Este equipamento ainda não é o próximo da sequência.\n\nDica: {hint}"
                : "Este equipamento ainda não é o próximo da sequência. Siga a ordem correta das tarefas.";

            PopupService.Instance?.ShowWarning(wrongOrderTitle, body, autoCloseSeconds);
        }
    }
}
