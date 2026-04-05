using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Data.Enums;
using SafetyProto.Gameplay.PPE;
using SafetyProto.Gameplay.Task;
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
        [SerializeField] private string ppeTitle     = "EPI Incorreto";

        private TaskManager _taskManager;

        private void Start()
        {
            _taskManager = FindFirstObjectByType<TaskManager>();
            if (_taskManager == null)
                SafetyLog.Warning("[TaskFeedbackController] TaskManager não encontrado.", this);

            if (!this.IsEventBusReady()) return;

            EventBus.Instance.onTaskTimeout.AddListener(OnTaskTimeout);
            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);

            foreach (var slot in snapSlots)
                if (slot != null)
                    slot.onDistractorSnapAttempted.AddListener(OnDistractorSnapAttempted);
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
                    slot.onDistractorSnapAttempted.RemoveListener(OnDistractorSnapAttempted);
        }

        private void OnTaskTimeout(TaskEventArgs args)
        {
            var text = args.Task?.hintText;
            if (string.IsNullOrWhiteSpace(text)) return;
            PopupService.Instance?.ShowNormal(hintTitle, text);
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.RuntimeTask == null) return;

            switch (args.RuntimeTask.State)
            {
                case TaskState.CompletedSuccessButUnsafe:
                    var ppeText = args.Task?.ppeAdvice;
                    if (!string.IsNullOrWhiteSpace(ppeText))
                        PopupService.Instance?.ShowWarning(ppeTitle, ppeText);
                    break;

                case TaskState.CompletedFailure:
                    var failText = args.Task?.failureAdvice;
                    if (!string.IsNullOrWhiteSpace(failText))
                        PopupService.Instance?.ShowWarning(failureTitle, failText);
                    break;
            }
        }

        private void OnDistractorSnapAttempted(PPEType attempted)
        {
            var advice = _taskManager?.CurrentRuntimeTask?.TaskData?.ppeAdvice;

            var text = !string.IsNullOrWhiteSpace(advice)
                ? advice
                : "Este equipamento não é adequado para trabalho em altura.";

            PopupService.Instance?.ShowWarning(ppeTitle, text);
        }
    }
}
