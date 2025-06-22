using UnityEngine;

public class ScoreManagerAdapter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("ScriptableObject holding the shared scoring service.")]
    public ScoreServiceSO scoreServiceAsset;
    [Tooltip("The manager that tracks which PPE the user is wearing.")]
    [SerializeField] private PPEManager ppeManager;

    private IScoreService _scoreService;
    private SafetyTask _currentTaskData; // Just holds a reference to the current task's data

    private void Awake()
    {
        if (scoreServiceAsset == null || ppeManager == null)
        {
            Debug.LogError("ScoreManagerAdapter is missing required references (ScoreService or PPEManager).", this);
            enabled = false;
            return;
        }
        _scoreService = scoreServiceAsset.Service;
    }

    private void OnEnable()
    {
        if (!this.IsEventBusReady()) return;

        EventBus.Instance.onTaskStarted.AddListener(HandleTaskStarted);
        EventBus.Instance.onTaskCompleted.AddListener(HandleTaskCompleted);
        EventBus.Instance.onTaskTimeout.AddListener(HandleTaskTimeout);
        EventBus.Instance.onActionAttempt.AddListener(HandleActionAttempt);
        _scoreService.ScoreChanged += HandleScoreChanged;
    }

    private void OnDisable()
    {
        if (EventBus.Instance == null) return;
        EventBus.Instance.onTaskStarted.RemoveListener(HandleTaskStarted);
        EventBus.Instance.onTaskCompleted.RemoveListener(HandleTaskCompleted);
        EventBus.Instance.onTaskTimeout.RemoveListener(HandleTaskTimeout);
        EventBus.Instance.onActionAttempt.RemoveListener(HandleActionAttempt);

        if (_scoreService != null)
            _scoreService.ScoreChanged -= HandleScoreChanged;
    }

    private void HandleTaskStarted(TaskEventArgs args)
    {
        _currentTaskData = args.Task;
    }

    // Handles scoring for a successfully completed task
    private void HandleTaskCompleted(TaskEventArgs args)
    {
        if (args.Task != null)
        {
            _scoreService.AddPoints(args.Task.successPoints, $"Task '{args.Task.taskName}' completed");
        }
        _currentTaskData = null; // The task is done, clear the reference.
    }

    // Handles scoring for a timed-out task
    private void HandleTaskTimeout(TaskEventArgs args)
    {
        if (args.Task != null)
        {
            _scoreService.SubtractPoints(args.Task.failurePenalty, $"Task '{args.Task.taskName}' timed out");
        }
        _currentTaskData = null;
    }

    private void HandleActionAttempt(ActionAttemptEventArgs args)
    {
        if (_currentTaskData == null)
        {
            Debug.Log($"Ignoring action '{args.ActionType}' as no task is currently active.");
            return;
        }

        bool isActionCorrect = (args.ActionType == _currentTaskData.expectedAction);
        if (isActionCorrect)
        {
            bool arePpeRequirementsMet = ppeManager.AreAllRequiredPPEWorn(_currentTaskData.requiredPPE);
            if (arePpeRequirementsMet)
            {
                // Action is perfect. Announce task completion.
                // TaskManager will hear this and change the state.
                EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs(_currentTaskData));
            }
            else
            {
                _scoreService.SubtractPoints(_currentTaskData.ppePenalty, "Action correct, but required PPE was missing");
            }
        }
        else
        {
            _scoreService.SubtractPoints(_currentTaskData.failurePenalty, $"Incorrect action for task '{_currentTaskData.taskName}'");
        }
    }

    private void HandleScoreChanged(int newScore, int delta, string reason)
    {
        Debug.Log($"[Score] {reason}: {(delta >= 0 ? "+" : "")}{delta} (Total: {newScore})");
    }
}