using SafetyProto.Core;
using SafetyProto.Gameplay.Task;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    public class SessionCompleteUI : MonoBehaviour
    {
        [Header("UI Elements")]
        public TextMeshProUGUI timeText;
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI taskSummaryText;

        private void OnEnable()
        {
            if (!this.IsEventBusReady())
                return;

            TryDisplayStoredSummary();
        }

        private void TryDisplayStoredSummary()
        {
            var taskManager = FindFirstObjectByType<TaskManager>();
            if (taskManager == null) return;

            var summaryOpt = taskManager.LastSessionSummary;
            if (summaryOpt.HasValue)
            {
                DisplaySummary(summaryOpt.Value);
            }
        }

        private void DisplaySummary(SessionCompletedEventArgs args)
        {
            int minutes = Mathf.FloorToInt(args.totalElapsedTime / 60f);
            int seconds = Mathf.FloorToInt(args.totalElapsedTime % 60f);
            string formattedTime = $"{minutes:00}:{seconds:00}";

            timeText.text = $"Time: {formattedTime}";
            scoreText.text = $"Score: {args.totalScore}";
            taskSummaryText.text = $"Tasks Completed: {args.tasksCompleted} / {args.totalTasks}";
        }
    }
}
