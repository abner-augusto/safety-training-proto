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
        [SerializeField] private string hintTitle    = "Dica";
        [SerializeField] private string failureTitle = "Atenção";
        [SerializeField] private string ppeTitle = "EPI Incorreto";
        [SerializeField] private string wrongOrderTitle = "Ordem Incorreta";

        [Header("Auto-fechamento")]
        [Tooltip("Tempo (s) para auto-fechar os alertas de task/EPI (ordem incorreta, EPI errado, etc.). 0 = sem timeout.")]
        [SerializeField] private float autoCloseSeconds = 6f;

        private TaskManager _taskManager;

        private void Start()
        {
            _taskManager = FindFirstObjectByType<TaskManager>();
            if (_taskManager == null)
                SafetyLog.Warning("[TaskFeedbackController] TaskManager not found.", this);

            if (!this.IsEventBusReady()) return;

            EventBus.Instance.onTaskTimeout.AddListener(OnTaskTimeout);
            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);

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
                EventBus.Instance.onTaskTimeout.RemoveListener(OnTaskTimeout);
                EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
            }

            foreach (var slot in snapSlots)
                if (slot != null)
                {
                    slot.onDistractorSnapAttempted.RemoveListener(OnDistractorSnapAttempted);
                    slot.onWrongOrderSnapAttempted.RemoveListener(OnWrongOrderSnapAttempted);
                }
        }

        private void OnTaskTimeout(TaskEventArgs args)
        {
            var text = args.Task?.hintText;
            if (string.IsNullOrWhiteSpace(text)) return;
            PopupService.Instance?.ShowNormal(hintTitle, text, autoCloseSeconds);
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.RuntimeTask == null) return;

            switch (args.RuntimeTask.State)
            {
                case TaskState.CompletedSuccessButUnsafe:
                    var ppeText = args.Task?.ppeAdvice;
                    if (!string.IsNullOrWhiteSpace(ppeText))
                        PopupService.Instance?.ShowWarning(ppeTitle, ppeText, autoCloseSeconds);
                    break;

                case TaskState.CompletedFailure:
                    var failText = args.Task?.failureAdvice;
                    if (!string.IsNullOrWhiteSpace(failText))
                        PopupService.Instance?.ShowWarning(failureTitle, failText, autoCloseSeconds);
                    break;
            }
        }

        private void OnDistractorSnapAttempted(PPEType attempted)
        {
            var task = _taskManager?.CurrentRuntimeTask?.TaskData;

            var advice = task?.ppeAdvice;
            var body = !string.IsNullOrWhiteSpace(advice)
                ? advice
                : "Este equipamento não é adequado para trabalho em altura.";

            // Anexa a dica da tarefa atual para reforçar o EPI correto.
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
