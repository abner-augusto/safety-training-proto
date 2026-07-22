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

        [Header("Bar Colors")]
        [SerializeField] private Color successColor  = new Color(0.153f, 0.682f, 0.376f); // #27AE60
        [SerializeField] private Color unsafeColor   = new Color(0.953f, 0.612f, 0.071f); // #F39C12
        [SerializeField] private Color failureColor  = new Color(0.906f, 0.298f, 0.235f); // #E74C3C
        [SerializeField] private Color notTriedColor = new Color(0.365f, 0.427f, 0.494f); // #5D6D7E

        public void Setup(int order, RuntimeSafetyTask runtimeTask, int fullPoints, int earnedPoints)
        {
            taskLabel.text = $"Tarefa {order}: {runtimeTask.taskName}";

            Color barColor = GetBarColor(runtimeTask.State);
            float fill = fullPoints > 0 ? Mathf.Clamp01(earnedPoints / (float)fullPoints) : 0f;

            progressBarFill.fillAmount = fill;
            progressBarFill.color = barColor;

            pointsText.text = earnedPoints > 0 ? $"+{earnedPoints} pts"
                            : earnedPoints < 0 ? $"{earnedPoints} pts"
                            : "0 pts";
            pointsText.color = barColor;
        }

        private Color GetBarColor(TaskState state)
        {
            return state switch
            {
                TaskState.CompletedSuccess        => successColor,
                TaskState.CompletedSuccessButUnsafe => unsafeColor,
                TaskState.CompletedFailure        => failureColor,
                _                                 => notTriedColor
            };
        }
    }
}
