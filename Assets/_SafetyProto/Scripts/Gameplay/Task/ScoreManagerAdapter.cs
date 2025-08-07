using UnityEngine;
using System;
using System.Collections.Generic;

public class ScoreManagerAdapter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("ScriptableObject holding the shared scoring service.")]
    public ScoreServiceSO scoreServiceAsset;
    [Tooltip("The manager that tracks which PPE the user is wearing.")]
    [SerializeField] private PPEManager ppeManager;

    private IScoreService _scoreService;
    private SafetyTask _currentTaskData; // Reference to the current task's data

    // Flags for penalties applied per task
    [Flags]
    private enum TaskPenaltyFlags
    {
        None = 0,
        ActionFailed = 1 << 0,
        PpeMissing = 1 << 1
    }

    // Track penalty flags per task
    private readonly Dictionary<SafetyTask, TaskPenaltyFlags> _taskPenaltyFlags = new Dictionary<SafetyTask, TaskPenaltyFlags>();

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
        Debug.Log($"[ScoreManagerAdapter] OnEnable: EventBus ready? {this.IsEventBusReady()}");
        if (_scoreService == null || EventBus.Instance == null || !this.IsEventBusReady())
            return;

        Debug.Log("[ScoreManagerAdapter] Subscribing to EventBus events");
        EventBus.Instance.onTaskStarted.AddListener(HandleTaskStarted);
        EventBus.Instance.onTaskCompleted.AddListener(HandleTaskCompleted);
        EventBus.Instance.onTaskTimeout.AddListener(HandleTaskTimeout);
        EventBus.Instance.onActionAttempt.AddListener(HandleActionAttempt);
        _scoreService.ScoreChanged += HandleScoreChanged;
    }

    private void OnDisable()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.onTaskStarted.RemoveListener(HandleTaskStarted);
            EventBus.Instance.onTaskCompleted.RemoveListener(HandleTaskCompleted);
            EventBus.Instance.onTaskTimeout.RemoveListener(HandleTaskTimeout);
            EventBus.Instance.onActionAttempt.RemoveListener(HandleActionAttempt);
        }

        if (_scoreService != null)
            _scoreService.ScoreChanged -= HandleScoreChanged;
    }

    private void HandleTaskStarted(TaskEventArgs args)
    {
        Debug.Log($"[ScoreManagerAdapter] HandleTaskStarted: starting '{args.Task.taskName}'");
        _currentTaskData = args.Task;
        _taskPenaltyFlags[_currentTaskData] = TaskPenaltyFlags.None;
    }

    private void HandleTaskCompleted(TaskEventArgs args)
    {
        Debug.Log($"[ScoreManagerAdapter] HandleTaskCompleted: completed '{args.Task.taskName}'");
        if (args.Task != null)
            _scoreService.AddPoints(args.Task.successPoints, $"Task '{args.Task.taskName}' completed");

        _taskPenaltyFlags.Remove(args.Task);
        _currentTaskData = null;
    }

    private void HandleTaskTimeout(TaskEventArgs args)
    {
        Debug.Log($"[ScoreManagerAdapter] HandleTaskTimeout: timed out '{args.Task.taskName}'");
        if (args.Task != null)
            _scoreService.SubtractPoints(args.Task.failurePenalty, $"Task '{args.Task.taskName}' timed out");

        _taskPenaltyFlags.Remove(args.Task);
        _currentTaskData = null;
    }

    private void HandleActionAttempt(ActionAttemptEventArgs args)
    {
        Debug.Log($"[ScoreManagerAdapter] HandleActionAttempt: action {args.ActionType} on '{_currentTaskData?.taskName ?? "<none>"}'");
        if (_currentTaskData == null)
        {
            Debug.LogWarning($"[ScoreManagerAdapter] Ignoring action '{args.ActionType}' as no task is active.");
            return;
        }

        bool isActionCorrect = (args.ActionType == _currentTaskData.expectedAction);
        var flags = _taskPenaltyFlags[_currentTaskData];

        if (isActionCorrect)
        {
            bool arePpeRequirementsMet = ppeManager.AreAllRequiredPPEWorn(_currentTaskData.requiredPPE);
            Debug.Log($"[ScoreManagerAdapter] PPE check for '{_currentTaskData.taskName}': {arePpeRequirementsMet}");

            // Apply PPE penalty only once if PPE was missing
            if (!arePpeRequirementsMet && !flags.HasFlag(TaskPenaltyFlags.PpeMissing))
            {
                Debug.Log($"[ScoreManagerAdapter] Applying PPE penalty of {_currentTaskData.ppePenalty} for '{_currentTaskData.taskName}'");
                _scoreService.SubtractPoints(
                    _currentTaskData.ppePenalty,
                    "Action correct, but required PPE was missing"
                );
                _taskPenaltyFlags[_currentTaskData] = flags | TaskPenaltyFlags.PpeMissing;
            }

            // Always complete a task if the action is correct
            EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs(_currentTaskData));
        }
        else if (!flags.HasFlag(TaskPenaltyFlags.ActionFailed))
        {
            Debug.Log($"[ScoreManagerAdapter] Applying failure penalty of {_currentTaskData.failurePenalty} for '{_currentTaskData.taskName}'");
            _scoreService.SubtractPoints(
                _currentTaskData.failurePenalty,
                $"Incorrect action for task '{_currentTaskData.taskName}'"
            );
            _taskPenaltyFlags[_currentTaskData] = flags | TaskPenaltyFlags.ActionFailed;
        }
    }

    private void HandleScoreChanged(int newScore, int delta, string reason)
    {
        Debug.Log($"[ScoreManagerAdapter] [Score] {reason}: {(delta >= 0 ? "+" : "")}{delta} (Total: {newScore})");
    }
}
