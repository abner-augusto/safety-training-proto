using UnityEngine;
using System.Collections.Generic; // For List in SafetyTask

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; } // Simple Singleton

    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;

    [Header("Dependencies - Optional, can get via FindObjectOfType")]
    [Tooltip("PPEManager reference, needed to check PPE status.")]
    public PPEManager ppeManager;
    // TaskManager not strictly needed if we get current task from TaskStartedEvent

    private int _totalScore = 0;
    private SafetyTask _currentTaskForScoring; // Store the current task when it starts

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // If you want it to persist across scenes
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (eventBus == null)
        {
            Debug.LogError("EventBus not assigned to ScoreManager!", this);
            enabled = false;
            return;
        }
        if (ppeManager == null)
        {
            ppeManager = FindObjectOfType<PPEManager>();
            if (ppeManager == null)
            {
                Debug.LogError("PPEManager not found by ScoreManager!", this);
                enabled = false;
                return;
            }
        }

        // Subscribe to relevant events
        eventBus.OnTaskStarted.AddListener(HandleTaskStarted);
        eventBus.OnTaskCompleted.AddListener(HandleTaskCompleted);
        eventBus.OnTaskTimeout.AddListener(HandleTaskTimeout);
        eventBus.OnActionAttempt.AddListener(HandleActionAttempt);
        // PPEStateChanged might not be directly needed for scoring if we check PPE at action time,
        // but could be used for continuous penalties or specific scoring rules.
        // eventBus.OnPPEStateChanged.AddListener(HandlePPEStateChanged);

        ResetScore();
    }

    private void OnDestroy()
    {
        if (eventBus != null)
        {
            eventBus.OnTaskStarted.RemoveListener(HandleTaskStarted);
            eventBus.OnTaskCompleted.RemoveListener(HandleTaskCompleted);
            eventBus.OnTaskTimeout.RemoveListener(HandleTaskTimeout);
            eventBus.OnActionAttempt.RemoveListener(HandleActionAttempt);
            // eventBus.OnPPEStateChanged.RemoveListener(HandlePPEStateChanged);
        }
    }

    public void ResetScore()
    {
        _totalScore = 0;
        eventBus.RaiseScoreChanged(new ScoreChangedEventArgs(_totalScore, 0));
    }

    private void HandleTaskStarted(TaskEventArgs args)
    {
        _currentTaskForScoring = args.Task;
        Debug.Log($"ScoreManager: Noted start of task '{_currentTaskForScoring.taskName}' for scoring.");
    }

    // This handles scoring for successful task completion via the CORRECT action.
    // The TaskManager raises TaskCompleted when the correct action is performed.
    private void HandleTaskCompleted(TaskEventArgs args)
    {
        if (args.Task == null) return;

        // This event is fired AFTER the correct action was identified by TaskManager.
        // The core success points are awarded here. PPE check was implicitly part of ActionAttempt.
        // However, the spec says "If Action matches expected within time and PPE OK -> +task.successPoints"
        // This implies the ActionAttemptEvent should be the primary point for awarding success points.
        // TaskCompleted becomes more of a "task is now over, positive outcome" signal.
        // Let's stick to the spec: ActionAttempt handles the main scoring.
        // TaskCompleted might not need to award points if ActionAttempt did it.
        // For simplicity now, let's assume TaskCompleted is just a signal.
        // Or, it awards points if the ActionAttempt logic doesn't, for more complex tasks.

        // Re-evaluating: If TaskManager fires TaskCompleted *only* on correct action,
        // then this is a good place to add success points.
        // The "within time" is handled by TimerSystem (TaskTimeout).
        // The "PPE OK" is handled in HandleActionAttempt.
        // This seems redundant if HandleActionAttempt already covers it.
        // Let's assume points for correct action are given in HandleActionAttempt.
        // This event is then a confirmation.

        Debug.Log($"ScoreManager: Task '{args.Task.taskName}' completed. Score processing for this event is currently minimal as ActionAttempt handles points.");
    }

    private void HandleTaskTimeout(TaskEventArgs args)
    {
        if (args.Task == null) return;
        Debug.Log($"ScoreManager: Task '{args.Task.taskName}' timed out. Applying penalty.");
        ApplyScoreChange(-args.Task.failurePenalty, "Task Timeout");
    }

    private void HandleActionAttempt(ActionAttemptEventArgs actionArgs)
    {
        if (_currentTaskForScoring == null)
        {
            Debug.LogWarning("ScoreManager: ActionAttempt received but no current task for scoring.");
            // Potentially a penalty for actions outside of a task context, if desired.
            // ApplyScoreChange(-5, "Action outside task");
            return;
        }

        int scoreDelta = 0;
        string reason = "";

        bool ppeOk = ppeManager.AreAllRequiredPPEWorn(_currentTaskForScoring.requiredPPE);

        if (actionArgs.ActionType == _currentTaskForScoring.expectedAction)
        {
            // Correct action
            if (ppeOk)
            {
                scoreDelta = _currentTaskForScoring.successPoints;
                reason = $"Correct Action ({actionArgs.ActionType}) with PPE";
            }
            else
            {
                // Correct action, but PPE missing.
                // Award success points for action, then penalize for PPE.
                // Or, spec implies this case might not get success points directly.
                // "If Action matches expected ... and PPE OK -> +task.successPoints"
                // This means if PPE is NOT OK, you don't get success points for the action itself,
                // just the PPE penalty. This is harsher. Let's follow this interpretation.
                scoreDelta = -_currentTaskForScoring.ppePenalty;
                reason = $"Correct Action ({actionArgs.ActionType}) but MISSING PPE";
                // Optionally, still give *some* points for the action, then subtract PPE penalty.
                // e.g., scoreDelta = _currentTaskForScoring.successPoints / 2 - _currentTaskForScoring.ppePenalty;
            }
        }
        else
        {
            // Wrong action
            scoreDelta = -_currentTaskForScoring.failurePenalty;
            reason = $"Wrong Action ({actionArgs.ActionType}), expected {_currentTaskForScoring.expectedAction}";
            if (!ppeOk)
            {
                scoreDelta -= _currentTaskForScoring.ppePenalty;
                reason += " and MISSING PPE";
            }
        }

        if (scoreDelta != 0)
        {
            ApplyScoreChange(scoreDelta, reason);
        }
    }

    private void ApplyScoreChange(int delta, string reason)
    {
        _totalScore += delta;
        eventBus.RaiseScoreChanged(new ScoreChangedEventArgs(_totalScore, delta));
        Debug.Log($"ScoreManager: Score change {delta}. Reason: {reason}. New Total Score: {_totalScore}");
    }

    public int GetCurrentScore()
    {
        return _totalScore;
    }
}