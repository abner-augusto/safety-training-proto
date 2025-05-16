using UnityEngine;
using System; // Needed for Action
using System.Collections.Generic; // Needed for List

// Manages the player's score, listens to task and timer events
public class ScoreManager : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static ScoreManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Don't destroy on load if score persists across scenes
            // DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    // --- End Singleton Pattern ---

    private int totalScore = 0;
    public int TotalScore => totalScore;

    // Events
    public event Action<int> OnScoreUpdated; // Notifies UI when total score changes
    public event Action<ScoreEntry> OnScoreEventLogged; // Notifies UI/DataLogger about individual score changes

    // --- Evaluation Methods (Called by TaskManager/TimerSystem) ---

    // Evaluates a player action attempt
    public void EvaluateAction(TaskDef currentTask, ActionEvent attemptedAction)
    {
        if (currentTask == null)
        {
            Debug.LogWarning("ScoreManager: Cannot evaluate action, current task is null.");
            return;
        }

        int pointsChange = 0;
        ScoreReason reason;
        string details = "";

        bool actionCorrect = (attemptedAction.type == currentTask.expectedAction);

        if (actionCorrect)
        {
            // Action was correct, now check PPE
            bool ppeWorn = PPEManager.Instance.AreAllWearing(currentTask.requiredPPE);

            if (ppeWorn)
            {
                pointsChange = currentTask.successPoints;
                reason = ScoreReason.Success;
                details = "Correct action with PPE compliance.";
                Debug.Log($"ScoreManager: Correct action ({currentTask.expectedAction}) completed with required PPE.");
            }
            else
            {
                // Action correct, but PPE was missing
                pointsChange = -currentTask.ppePenalty; // Penalties are typically negative
                reason = ScoreReason.MissingPPE;
                // Find which PPE was missing for details
                List<ProtectionType> missing = new List<ProtectionType>();
                if (currentTask.requiredPPE != null)
                {
                    foreach (var ppeType in currentTask.requiredPPE)
                    {
                        if (ppeType != ProtectionType.None && !PPEManager.Instance.isWearing(ppeType))
                        {
                            missing.Add(ppeType);
                        }
                    }
                }
                details = $"Correct action but missing PPE: {string.Join(", ", missing)}";

                Debug.LogWarning($"ScoreManager: Correct action ({currentTask.expectedAction}) but missing PPE. Penalty: {pointsChange}");
            }
        }
        else
        {
            // Action was incorrect
            pointsChange = -currentTask.failurePenalty; // Penalties are typically negative
            reason = ScoreReason.IncorrectAction;
            details = $"Attempted {attemptedAction.type}, Expected {currentTask.expectedAction}";
            Debug.LogWarning($"ScoreManager: Incorrect action attempted. Penalty: {pointsChange}");
        }

        // Update score and log
        UpdateScore(pointsChange, currentTask.taskName, reason, details);
    }

    // Evaluates the task when the timer runs out
    public void EvaluateTimeout(TaskDef timedOutTask)
    {
        if (timedOutTask == null)
        {
            Debug.LogWarning("ScoreManager: Cannot evaluate timeout, timed out task is null.");
            return;
        }

        // Apply penalty for timeout
        int pointsChange = -timedOutTask.failurePenalty; // Timeout usually results in failure penalty
        ScoreReason reason = ScoreReason.Timeout;
        string details = "Task time limit exceeded.";

        Debug.LogWarning($"ScoreManager: Task '{timedOutTask.taskName}' timed out. Penalty: {pointsChange}");

        // Update score and log
        UpdateScore(pointsChange, timedOutTask.taskName, reason, details);

        // Note: PPE check on timeout might not be necessary depending on rules,
        // as the failure is the timeout itself.
    }

    // --- Internal Score Management ---

    private void UpdateScore(int pointsChange, string taskName, ScoreReason reason, string details)
    {
        totalScore += pointsChange;

        // Create score entry
        ScoreEntry newEntry = new ScoreEntry(taskName, pointsChange, reason, details);

        // Log the event
        DataLogger.Instance?.LogScoreEvent(newEntry);

        // Notify listeners
        OnScoreEventLogged?.Invoke(newEntry); // For DataLogger (if not logging directly) and UI
        OnScoreUpdated?.Invoke(totalScore); // For UI
    }

    // --- Helper Methods ---
    public void ResetScore()
    {
        totalScore = 0;
        Debug.Log("ScoreManager: Score reset to 0.");
        DataLogger.Instance?.ClearLogInMemory(); // Optionally clear log on score reset
        OnScoreUpdated?.Invoke(totalScore);
    }
}