using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; } // Simple Singleton

    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;

    [Header("Dependencies - Optional, can get via FindObjectOfType")]
    [Tooltip("PPEManager reference, needed to check PPE status.")]
    public PPEManager ppeManager;

    private int _totalScore = 0;
    private SafetyTask _currentTaskForScoring; // Store the current task when it starts

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (eventBus == null)
        {
            Debug.LogError("EventBus not assigned to ScoreManager!", this);
            enabled = false;
            return;
        }
        if (ppeManager == null)
        {
            ppeManager = Object.FindFirstObjectByType<PPEManager>();
            if (ppeManager == null)
            {
                Debug.LogError("PPEManager not found by ScoreManager!", this);
                enabled = false;
                return;
            }
        }

        // Subscribe to relevant events
        eventBus.onTaskStarted.AddListener(HandleTaskStarted);
        eventBus.onTaskCompleted.AddListener(HandleTaskCompleted);
        eventBus.onTaskTimeout.AddListener(HandleTaskTimeout);
        eventBus.onActionAttempt.AddListener(HandleActionAttempt);

        ResetScore();
    }

    private void OnDestroy()
    {
        if (eventBus != null)
        {
            eventBus.onTaskStarted.RemoveListener(HandleTaskStarted);
            eventBus.onTaskCompleted.RemoveListener(HandleTaskCompleted);
            eventBus.onTaskTimeout.RemoveListener(HandleTaskTimeout);
            eventBus.onActionAttempt.RemoveListener(HandleActionAttempt);
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

    private void HandleTaskCompleted(TaskEventArgs args)
    {
        if (args.Task == null) return;
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
            return;
        }

        int scoreDelta = 0;
        string reason = "";

        bool ppeOk = ppeManager.AreAllRequiredPPEWorn(_currentTaskForScoring.requiredPPE);

        if (actionArgs.ActionType == _currentTaskForScoring.expectedAction)
        {
            if (ppeOk)
            {
                scoreDelta = _currentTaskForScoring.successPoints;
                reason = $"Correct Action ({actionArgs.ActionType}) with PPE";
            }
            else
            {
                scoreDelta = -_currentTaskForScoring.ppePenalty;
                reason = $"Correct Action ({actionArgs.ActionType}) but MISSING PPE";
            }
        }
        else
        {
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