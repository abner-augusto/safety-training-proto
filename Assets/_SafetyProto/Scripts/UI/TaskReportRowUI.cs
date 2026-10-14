using SafetyProto.Core;
using RuntimeSafetyTask = SafetyProto.Core.RuntimeSafetyTask;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    public class TaskReportRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text taskLabel;
        [SerializeField] private Image scoreBadge;
        [SerializeField] private TMP_Text pointsText;
        [SerializeField] private GameObject adviceContainer;
        [SerializeField] private TMP_Text adviceText;

        [Header("Badge Colors")]
        [SerializeField] private Color successColor  = new Color(0.161f, 0.702f, 0.396f); // #29B365
        [SerializeField] private Color unsafeColor   = new Color(0.941f, 0.682f, 0.247f); // #F0AE3F
        [SerializeField] private Color notTriedColor = new Color(0.365f, 0.427f, 0.494f); // #5D6D7E

        public void Setup(RuntimeSafetyTask runtimeTask, int earnedPoints, string advice)
        {
            taskLabel.text = runtimeTask.taskName;

            Color badgeColor = GetBadgeColor(runtimeTask);
            scoreBadge.color = badgeColor;

            pointsText.text = earnedPoints > 0 ? $"+{earnedPoints} pts" : "0 pts";

            bool hasAdvice = !string.IsNullOrEmpty(advice);
            adviceContainer.SetActive(hasAdvice);
            if (hasAdvice) adviceText.text = advice;

            // Forces the row's ContentSizeFitter to run; otherwise only rows whose advice box toggled get laid out.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        private Color GetBadgeColor(RuntimeSafetyTask runtimeTask)
        {
            if (!runtimeTask.IsValid || runtimeTask.TaskData == null) return notTriedColor;

            return runtimeTask.State == TaskState.CompletedSuccess && !runtimeTask.HasMissedPPEOnce
                ? successColor
                : unsafeColor;
        }
    }
}
