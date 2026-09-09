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

            // The row owns its own height through a ContentSizeFitter, and the list parent
            // deliberately does not control it. Without this the fitter only runs for rows whose
            // advice box toggled, leaving every other row at its uninitialised instantiation size.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        // Grey is reserved for invalid/unavailable task data, never for a task the participant
        // simply did not perform — an omission is an actionable issue and stays amber.
        private Color GetBadgeColor(RuntimeSafetyTask runtimeTask)
        {
            if (!runtimeTask.IsValid || runtimeTask.TaskData == null) return notTriedColor;

            return runtimeTask.State == TaskState.CompletedSuccess && !runtimeTask.HasMissedPPEOnce
                ? successColor
                : unsafeColor;
        }
    }
}
