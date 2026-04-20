using SafetyProto.Core;
using RuntimeSafetyTask = SafetyProto.Core.RuntimeSafetyTask;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    public class TaskReportRowUI : MonoBehaviour
    {
        [SerializeField] private Image taskIcon;
        [SerializeField] private TMP_Text taskLabel;
        [SerializeField] private Image progressBarFill;
        [SerializeField] private TMP_Text pointsText;

        private static readonly Color ColorSuccess  = new Color(0.153f, 0.682f, 0.376f); // #27AE60
        private static readonly Color ColorUnsafe   = new Color(0.953f, 0.612f, 0.071f); // #F39C12
        private static readonly Color ColorFailure  = new Color(0.906f, 0.298f, 0.235f); // #E74C3C
        private static readonly Color ColorNotTried = new Color(0.365f, 0.427f, 0.494f); // #5D6D7E

        public void Setup(int order, RuntimeSafetyTask runtimeTask, int maxPoints)
        {
            taskLabel.text = $"Task {order}: {runtimeTask.taskName}";

            Color barColor = GetBarColor(runtimeTask.State);
            float fill = CalculateFill(runtimeTask, maxPoints);

            progressBarFill.fillAmount = fill;
            progressBarFill.color = barColor;

            pointsText.text = FormatPoints(runtimeTask);
            pointsText.color = barColor;
        }

        private static Color GetBarColor(TaskState state)
        {
            return state switch
            {
                TaskState.CompletedSuccess        => ColorSuccess,
                TaskState.CompletedSuccessButUnsafe => ColorUnsafe,
                TaskState.CompletedFailure        => ColorFailure,
                _                                 => ColorNotTried
            };
        }

        private static float CalculateFill(RuntimeSafetyTask task, int maxPoints)
        {
            if (maxPoints <= 0) return 0f;

            return task.State switch
            {
                TaskState.CompletedSuccess => 1f,
                TaskState.CompletedSuccessButUnsafe =>
                    Mathf.Clamp01((float)(task.TaskData.successPoints - task.TaskData.ppePenalty) / maxPoints),
                _ => 0f
            };
        }

        private static string FormatPoints(RuntimeSafetyTask task)
        {
            return task.State switch
            {
                TaskState.CompletedSuccess =>
                    $"+{task.TaskData.successPoints} pts",
                TaskState.CompletedSuccessButUnsafe =>
                    $"+{Mathf.Max(0, task.TaskData.successPoints - task.TaskData.ppePenalty)} pts",
                TaskState.CompletedFailure =>
                    task.TaskData.failurePenalty > 0 ? $"-{task.TaskData.failurePenalty} pts" : "0 pts",
                _ => "0 pts"
            };
        }
    }
}
