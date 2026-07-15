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
        private static readonly Color ColorOmitted  = new Color(0.55f, 0.35f, 0.65f); // #8C59A6

        public void Setup(int order, RuntimeSafetyTask runtimeTask, int fullPoints, int earnedPoints)
        {
            taskLabel.text = $"Tarefa {order}: {runtimeTask.taskName}";

            Color barColor = GetBarColor(runtimeTask.State);
            float fill = fullPoints > 0 ? Mathf.Clamp01(earnedPoints / (float)fullPoints) : 0f;

            progressBarFill.fillAmount = fill;
            progressBarFill.color = barColor;

            pointsText.text = runtimeTask.State == TaskState.Omitted ? "Omitida — 0 pts"
                            : earnedPoints > 0 ? $"+{earnedPoints} pts"
                            : earnedPoints < 0 ? $"{earnedPoints} pts"
                            : "0 pts";
            pointsText.color = barColor;
        }

        private static Color GetBarColor(TaskState state)
        {
            return state switch
            {
                TaskState.CompletedSuccess        => ColorSuccess,
                TaskState.CompletedSuccessButUnsafe => ColorUnsafe,
                TaskState.CompletedFailure        => ColorFailure,
                TaskState.Omitted                 => ColorOmitted,
                _                                 => ColorNotTried
            };
        }
    }
}
