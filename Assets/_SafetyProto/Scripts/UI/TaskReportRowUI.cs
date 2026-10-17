using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Sessions;
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

        /// <summary>Fills the row. <paramref name="charges"/> are the penalties taken off this
        /// task; they are listed under the advice so earned points and the final score reconcile.</summary>
        public void Setup(RuntimeSafetyTask runtimeTask, int earnedPoints, string advice,
                          IReadOnlyList<ScoreCharge> charges, string orderLine)
        {
            taskLabel.text = runtimeTask.taskName;

            Color badgeColor = GetBadgeColor(runtimeTask);
            scoreBadge.color = badgeColor;

            pointsText.text = earnedPoints > 0 ? $"+{earnedPoints} pts" : "0 pts";

            ShowDetail(ReportComposition.BuildRowDetail(advice, charges, runtimeTask.CompletedOutOfOrder, orderLine));
        }

        /// <summary>Fills a row that carries charges only, for penalties that belong to no task.</summary>
        public void SetupChargesOnly(string label, IReadOnlyList<ScoreCharge> charges)
        {
            taskLabel.text = label;
            scoreBadge.color = unsafeColor;

            int total = 0;
            foreach (var charge in charges) total += charge.Amount;
            pointsText.text = $"-{total} pts";

            ShowDetail(ReportComposition.BuildRowDetail(string.Empty, charges, false, string.Empty));
        }

        private void ShowDetail(string detail)
        {
            bool hasDetail = !string.IsNullOrEmpty(detail);
            adviceContainer.SetActive(hasDetail);
            if (hasDetail) adviceText.text = detail;

            // Forces the row's ContentSizeFitter to run; otherwise only rows whose advice box toggled get laid out.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        private Color GetBadgeColor(RuntimeSafetyTask runtimeTask)
        {
            if (!runtimeTask.IsValid || runtimeTask.TaskData == null) return notTriedColor;

            return runtimeTask.State == TaskState.CompletedSuccess
                   && !runtimeTask.HasMissedPPEOnce
                   && !runtimeTask.HasMisclassifiedReport
                ? successColor
                : unsafeColor;
        }
    }
}
