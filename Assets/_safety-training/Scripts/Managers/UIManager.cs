using UnityEngine;
using TMPro; // Or UnityEngine.UI if using standard Text

// Presents information on world-space canvas or floating HUD
public class UIManager : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static UIManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }
    // --- End Singleton Pattern ---

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI taskDescriptionText;
    [SerializeField] private GameObject sessionSummaryPanel;
    [SerializeField] private TextMeshProUGUI summaryText; // For displaying final summary

    [Header("Dependencies")]
    [SerializeField] private TimerSystem timerSystem;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private TaskManager taskManager;
    [SerializeField] private DataLogger dataLogger; // To potentially display log info

    private void OnEnable()
    {
        // Subscribe to score updates
        if (scoreManager != null)
        {
            scoreManager.OnScoreUpdated += UpdateScoreDisplay;
            // scoreManager.OnScoreEventLogged += HandleScoreEventLogged; // Optional: detailed logs
        }
        else
        {
            Debug.LogError("UIManager: ScoreManager reference is missing!");
        }

        // Subscribe to task start events (for description)
        if (taskManager != null)
        {
            taskManager.OnTaskStarted += UpdateTaskDisplay; // TaskManager directly calls this now
            taskManager.OnAllTasksComplete += ShowSessionSummary;
        }
        else
        {
            Debug.LogError("UIManager: TaskManager reference is missing!");
        }

        // Subscribe to TimerSystem timeout (Optional, TimerSystem notifies TaskManager)
        // if (timerSystem != null) { timerSystem.OnTaskTimeout += HandleTimeoutUI; }
    }

    private void OnDisable()
    {
        // Unsubscribe
        if (scoreManager != null)
        {
            scoreManager.OnScoreUpdated -= UpdateScoreDisplay;
            // scoreManager.OnScoreEventLogged -= HandleScoreEventLogged;
        }
        if (taskManager != null)
        {
            taskManager.OnTaskStarted -= UpdateTaskDisplay;
            taskManager.OnAllTasksComplete -= ShowSessionSummary;
        }
        // if (timerSystem != null) { timerSystem.OnTaskTimeout -= HandleTimeoutUI; }
    }

    private void Start()
    {
        // Ensure summary is hidden initially
        if (sessionSummaryPanel != null)
        {
            sessionSummaryPanel.SetActive(false);
        }

        // Initial UI updates
        UpdateScoreDisplay(scoreManager != null ? scoreManager.TotalScore : 0);
        UpdateTimerDisplay(0); // Timer isn't running initially
        UpdateTaskDisplay(null); // No task initially
    }

    // Update is typically used for frequent updates like timer
    void Update()
    {
        // Poll the timer system for the remaining time
        if (timerSystem != null && timerSystem.IsRunning)
        {
            UpdateTimerDisplay(timerSystem.RemainingTime);
        }
        else if (timerText != null && !timerSystem.IsRunning)
        {
            // Optionally show "Stopped" or final time when not running
            // timerText.text = "Time: Stopped";
        }
    }

    // --- Public methods for Managers to call ---

    public void UpdateTimerDisplay(float timeRemaining)
    {
        if (timerText != null)
        {
            // Format time as MM:SS or SS.ms
            int minutes = Mathf.FloorToInt(timeRemaining / 60F);
            int seconds = Mathf.FloorToInt(timeRemaining % 60F);
            // int milliseconds = Mathf.FloorToInt((timeRemaining * 100F) % 100F); // For milliseconds

            timerText.text = $"Time: {minutes:00}:{seconds:00}";
            // timerText.text = $"Time: {seconds:00}.{milliseconds:00}";
        }
    }

    public void UpdateScoreDisplay(int newScore)
    {
        if (scoreText != null)
        {
            scoreText.text = $"Score: {newScore}";
        }
    }

    public void UpdateTaskDisplay(TaskDef task)
    {
        if (taskDescriptionText != null)
        {
            if (task != null)
            {
                taskDescriptionText.text = $"Task: {task.taskName}\n{task.taskDescription}\nTime Limit: {task.timeLimit}s";
                // Optional: Display required PPE
                if (task.requiredPPE != null && task.requiredPPE.Count > 0 && !(task.requiredPPE.Count == 1 && task.requiredPPE[0] == ProtectionType.None))
                {
                    taskDescriptionText.text += "\nRequired PPE: " + string.Join(", ", task.requiredPPE);
                }
            }
            else
            {
                taskDescriptionText.text = "No active task.";
            }
        }
    }

    public void ShowSessionSummary()
    {
        if (sessionSummaryPanel != null)
        {
            sessionSummaryPanel.SetActive(true);
            if (summaryText != null)
            {
                string summary = "--- Training Session Summary ---\n";
                summary += $"Total Score: {(scoreManager != null ? scoreManager.TotalScore : 0)}\n";
                summary += $"Tasks Completed: {(taskManager != null ? taskManager.CurrentTaskIndex : 0)} / {(taskManager != null ? taskManager.TotalTasks : 0)}\n";

                // Accuracy calculation is complex - needs tracking correct actions vs attempts
                // Placeholder for accuracy:
                // You'd need to track total action attempts and total *correct* actions
                // TaskManager would need fields for this. ScoreManager logs outcomes.
                // Example (requires tracking):
                // int totalAttempts = GetTotalActionAttempts(); // Need to implement this tracking
                // int correctAttempts = GetTotalCorrectActions(); // Need to implement this tracking
                // float accuracy = (totalAttempts > 0) ? (float)correctAttempts / totalAttempts * 100f : 0f;
                // summary += $"Accuracy: {accuracy:F1}%\n";
                summary += "Accuracy: (Requires Tracking)\n"; // Placeholder

                // Optional: List all score events from the log
                summary += "\n--- Score Log ---";
                if (dataLogger != null)
                {
                    var scoreEntries = dataLogger.GetScoreLog();
                    if (scoreEntries.Count > 0)
                    {
                        foreach (var entry in scoreEntries)
                        {
                            summary += $"\n{entry.timestamp:HH:mm:ss} | {entry.taskName} | {entry.pointsChange:+0;-0} | {entry.reason}";
                        }
                    }
                    else
                    {
                        summary += "\n(No score events recorded)";
                    }
                }


                summaryText.text = summary;
            }
            Debug.Log("UIManager: Showing session summary.");
        }
        // Hide main task UI elements
        if (timerText != null) timerText.gameObject.SetActive(false);
        if (scoreText != null) scoreText.gameObject.SetActive(false);
        if (taskDescriptionText != null) taskDescriptionText.gameObject.SetActive(false);
    }

    public void HideSessionSummary()
    {
        if (sessionSummaryPanel != null)
        {
            sessionSummaryPanel.SetActive(false);
            Debug.Log("UIManager: Hiding session summary.");
        }
        // Show main task UI elements again if needed (e.g., restarting)
        if (timerText != null) timerText.gameObject.SetActive(true);
        if (scoreText != null) scoreText.gameObject.SetActive(true);
        if (taskDescriptionText != null) taskDescriptionText.gameObject.SetActive(true);
    }

    // Optional: Handle individual score events visually (e.g., floating text)
    // private void HandleScoreEventLogged(ScoreEntry entry)
    // {
    //     Debug.Log($"UI Manager received score event: {entry}");
    //     // TODO: Implement logic to display floating text for points change
    // }
}