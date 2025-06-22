using UnityEngine;
using TMPro;

public class SessionCompleteUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI taskSummaryText;

    void Start()
    {
        if (!this.IsEventBusReady())
        {
            return;
        }

        EventBus.Instance.onSessionCompleted.AddListener(HandleSessionCompleted);
        gameObject.SetActive(false); // Hide initially
    }

    private void HandleSessionCompleted(SessionCompletedEventArgs args)
    {
        int minutes = Mathf.FloorToInt(args.totalElapsedTime / 60f);
        int seconds = Mathf.FloorToInt(args.totalElapsedTime % 60f);
        string formattedTime = $"{minutes:00}:{seconds:00}";

        timeText.text = $"Time: {formattedTime}";
        scoreText.text = $"Score: {args.totalScore}";
        taskSummaryText.text = $"Tasks Completed: {args.tasksCompleted} / {args.totalTasks}";

        gameObject.SetActive(true);
    }
}