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

    private IScoreService _scoreService;
    private readonly HashSet<string> _completedTaskIds = new();
    private SafetyTask _currentTask;

    private void Awake()
    {
        if (scoreServiceAsset == null)
        {
            Debug.LogError("ScoreService asset not assigned", this);
            enabled = false;
            return;
        }
        _scoreService = scoreServiceAsset.Service;
    }

    private void OnEnable()
    {
        if (EventBus.Instance == null)
        {
            Debug.LogError("EventBus instance missing", this);
            return;
        }

        EventBus.Instance.onTaskStarted.AddListener(HandleTaskStarted);
        EventBus.Instance.onTaskCompleted.AddListener(HandleTaskCompleted);
        EventBus.Instance.onActionAttempt.AddListener(HandleActionAttempt);
        EventBus.Instance.onTaskTimeout.AddListener(HandleTaskTimeout);

        _scoreService.ScoreChanged += HandleScoreChanged;
    }

    private void OnDisable()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.onTaskStarted.RemoveListener(HandleTaskStarted);
            EventBus.Instance.onTaskCompleted.RemoveListener(HandleTaskCompleted);
            EventBus.Instance.onActionAttempt.RemoveListener(HandleActionAttempt);
            EventBus.Instance.onTaskTimeout.RemoveListener(HandleTaskTimeout);
        }

        if (_scoreService != null)
            _scoreService.ScoreChanged -= HandleScoreChanged;
    }

    private void HandleTaskStarted(TaskEventArgs args)
    {
        if (args.Task == null) return;
        _currentTask = args.Task;
    }

    private void HandleTaskCompleted(TaskEventArgs args)
    {
        if (args.Task == null) return;
        string taskId = args.Task.taskName;
        if (_completedTaskIds.Contains(taskId))
        {
            Debug.LogWarning($"Task '{taskId}' already completed. Ignoring duplicate score.");
            return;
        }

        _completedTaskIds.Add(taskId);
        _scoreService.AddPoints(args.Task.successPoints, $"Task '{taskId}' completed");
        if (_currentTask == args.Task)
            _currentTask = null;
    }

    private void HandleTaskTimeout(TaskEventArgs args)
    {
        if (args.Task == null) return;
        string taskId = args.Task.taskName;
        if (_completedTaskIds.Contains(taskId))
        {
            Debug.LogWarning($"Task '{taskId}' already timed out. Ignoring duplicate penalty.");
            return;
        }

        _scoreService.SubtractPoints(args.Task.failurePenalty, $"Task '{taskId}' timed out");
        _completedTaskIds.Add(taskId);
        if (_currentTask == args.Task)
            _currentTask = null;
    }

    private void HandleActionAttempt(ActionAttemptEventArgs args)
    {
        if (_currentTask != null && args.ActionType == _currentTask.expectedAction)
        {
            string taskId = _currentTask.taskName;
            if (_completedTaskIds.Contains(taskId)) return;

            _scoreService.AddPoints(_currentTask.successPoints, $"Action '{args.ActionType}' succeeded");
            _completedTaskIds.Add(taskId);
        }
        else
        {
            _scoreService.SubtractPoints(5, $"Incorrect or duplicate action: {args.ActionType}");
        }
    }

    private void HandleScoreChanged(int newScore, int delta, string reason)
    {
        Debug.Log($"[Score] {reason}: {(delta >= 0 ? "+" : "")}{delta} (Total: {newScore})");
    }

    /// <summary>
    /// Clears the record of completed tasks, allowing for reset between sessions.
    /// </summary>
    public void ResetScoringSession()
    {
        _completedTaskIds.Clear();
    }
}