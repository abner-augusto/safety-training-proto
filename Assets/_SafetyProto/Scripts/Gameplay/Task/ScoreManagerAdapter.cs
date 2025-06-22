using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MonoBehaviour that bridges Unity gameplay events to a decoupled <see cref="IScoreService"/>.
/// </summary>
public class ScoreManagerAdapter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("ScriptableObject holding the shared scoring service.")]
    public ScoreServiceSO scoreServiceAsset;
    [Tooltip("The manager that tracks which PPE the user is wearing.")]
    [SerializeField] private PPEManager ppeManager;

    private IScoreService _scoreService;
    private readonly HashSet<SafetyTask> _completedTasks = new();
    private SafetyTask _currentTask;

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
        EventBus.Instance.onTaskCompleted.AddListener(HandleTaskCompletedScore);
        EventBus.Instance.onActionAttempt.AddListener(HandleActionAttempt);
        EventBus.Instance.onTaskTimeout.AddListener(HandleTaskTimeout);

        _scoreService.ScoreChanged += HandleScoreChanged;
    }

    private void OnDisable()
    {
        if (EventBus.Instance == null) return;

        EventBus.Instance.onTaskStarted.RemoveListener(HandleTaskStarted);
        EventBus.Instance.onTaskCompleted.RemoveListener(HandleTaskCompletedScore);
        EventBus.Instance.onActionAttempt.RemoveListener(HandleActionAttempt);
        EventBus.Instance.onTaskTimeout.RemoveListener(HandleTaskTimeout);

        if (_scoreService != null)
            _scoreService.ScoreChanged -= HandleScoreChanged;
    }

    private void HandleTaskStarted(TaskEventArgs args)
    {
        if (args.Task == null) return;
        _currentTask = args.Task;
    }

    /// <summary>
    /// This is the new central handler for all user actions.
    /// </summary>
    private void HandleActionAttempt(ActionAttemptEventArgs args)
    {
        if (_currentTask == null || _completedTasks.Contains(_currentTask))
        {
            Debug.Log($"Ignoring action '{args.ActionType}' as no task is currently active.");
            return;
        }

        bool isActionCorrect = (args.ActionType == _currentTask.expectedAction);
        bool arePpeRequirementsMet = ppeManager.AreAllRequiredPPEWorn(_currentTask.requiredPPE);

        if (isActionCorrect)
        {
            ProcessCorrectAction(arePpeRequirementsMet);
        }
        else
        {
            ProcessIncorrectAction();
        }
    }

    /// <summary>
    /// Handles the scoring and completion logic when the user performs the correct action.
    /// </summary>
    private void ProcessCorrectAction(bool ppeMet)
    {
        if (ppeMet)
        {
            // PERFECT: Correct action with correct PPE. Task is complete.
            Debug.Log($"Task '{_currentTask.taskName}' completed successfully.");
            
            // Announce that the task is complete. TaskManager will hear this.
            EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs(_currentTask));
        }
        else
        {
            // GOOD ACTION, WRONG PPE: Apply PPE penalty, but do not complete the task.
            _scoreService.SubtractPoints(_currentTask.ppePenalty, "Action correct, but required PPE was missing");
        }
    }

    /// <summary>
    /// Handles the scoring when the user performs the wrong action.
    /// </summary>
    private void ProcessIncorrectAction()
    {
        _scoreService.SubtractPoints(_currentTask.failurePenalty, $"Incorrect action for task '{_currentTask.taskName}'");
    }
    
    /// <summary>
    /// Applies score AFTER the task has been marked completed.
    /// </summary>
    private void HandleTaskCompletedScore(TaskEventArgs args)
    {
        if (args.Task == null || _completedTasks.Contains(args.Task)) return;

        _completedTasks.Add(args.Task);
        _scoreService.AddPoints(args.Task.successPoints, $"Task '{args.Task.taskName}' completed");
        
        if (_currentTask == args.Task)
            _currentTask = null;
    }

    private void HandleTaskTimeout(TaskEventArgs args)
    {
        if (args.Task == null || _completedTasks.Contains(args.Task)) return;

        _scoreService.SubtractPoints(args.Task.failurePenalty, $"Task '{args.Task.taskName}' timed out");
        _completedTasks.Add(args.Task);

        if (_currentTask == args.Task)
            _currentTask = null;
    }

    private void HandleScoreChanged(int newScore, int delta, string reason)
    {
        Debug.Log($"[Score] {reason}: {(delta >= 0 ? "+" : "")}{delta} (Total: {newScore})");
    }
}