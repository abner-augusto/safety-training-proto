using SafetyProto.Core;
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

        private static SessionCompletedEventArgs? _cachedSummary;

        private void OnEnable()
        {
            if (!this.IsEventBusReady())
                return;

            EventBus.Instance.onSessionCompleted.AddListener(OnSessionCompleted);

            if (_cachedSummary.HasValue)
            {
                DisplaySummary(_cachedSummary.Value);
            }
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onSessionCompleted.RemoveListener(OnSessionCompleted);
            }
        }

        private void OnSessionCompleted(SessionCompletedEventArgs args)
        {
            _cachedSummary = args;
            DisplaySummary(args);
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
