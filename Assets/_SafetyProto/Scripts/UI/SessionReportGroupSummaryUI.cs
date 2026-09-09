using System.Collections.Generic;
using SafetyProto.Core;
using RuntimeSafetyTask = SafetyProto.Core.RuntimeSafetyTask;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    /// <summary>
    /// One card in the finish-screen group-summary grid: an authored group name, its
    /// completed-of-total count, and a row of state dots, one per task in the group.
    /// </summary>
    public class SessionReportGroupSummaryUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text groupNameText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private Transform dotContainer;
        [SerializeField] private Image dotTemplate;

        [Header("Dot Colors")]
        [SerializeField] private Color successColor  = new Color(0.161f, 0.702f, 0.396f); // #29B365
        [SerializeField] private Color issueColor    = new Color(0.941f, 0.682f, 0.247f); // #F0AE3F
        [SerializeField] private Color invalidColor  = new Color(0.365f, 0.427f, 0.494f); // #5D6D7E

        public void Setup(string groupName, IReadOnlyList<RuntimeSafetyTask> groupTasks)
        {
            groupNameText.text = groupName;

            int completed = 0;
            foreach (var t in groupTasks)
                if (t.State == TaskState.CompletedSuccess || t.State == TaskState.CompletedSuccessButUnsafe)
                    completed++;

            countText.text = $"{completed} / {groupTasks.Count}";

            ClearGeneratedDots();

            foreach (var t in groupTasks)
            {
                var dot = Instantiate(dotTemplate, dotContainer);
                dot.gameObject.SetActive(true);
                dot.color = DotColorFor(t);
            }
        }

        private Color DotColorFor(RuntimeSafetyTask t)
        {
            if (!t.IsValid || t.TaskData == null) return invalidColor;

            return t.State == TaskState.CompletedSuccess && !t.HasMissedPPEOnce
                ? successColor
                : issueColor;
        }

        private void ClearGeneratedDots()
        {
            for (int i = dotContainer.childCount - 1; i >= 0; i--)
            {
                var child = dotContainer.GetChild(i);
                if (child == dotTemplate.transform) continue;
                Destroy(child.gameObject);
            }
        }
    }
}
