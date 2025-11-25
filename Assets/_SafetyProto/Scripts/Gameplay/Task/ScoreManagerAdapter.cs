using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.PPE;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.Task
{
    public class ScoreManagerAdapter : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("ScriptableObject holding the shared scoring service.")]
        public ScoreServiceSO scoreServiceAsset;
        [Tooltip("The manager that tracks which PPE the user is wearing.")]
        [SerializeField] private PPEManager ppeManager;

        private IScoreService _scoreService;
        private RuntimeSafetyTask _currentTask;

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
            if (_scoreService == null || EventBus.Instance == null || !this.IsEventBusReady())
                return;

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
            _currentTask = args.RuntimeTask ?? new RuntimeSafetyTask(args.Task);
        }

        private void HandleTaskCompleted(TaskEventArgs args)
        {
            if (args.Task != null)
                _scoreService.AddPoints(args.Task.successPoints, $"Task '{args.Task.taskName}' completed");

            _currentTask = null;
        }

        private void HandleTaskTimeout(TaskEventArgs args)
        {
            if (args.Task != null)
                _scoreService.SubtractPoints(args.Task.failurePenalty, $"Task '{args.Task.taskName}' timed out");

            _currentTask = null;
        }

        private void HandleActionAttempt(ActionAttemptEventArgs args)
        {
            if (_currentTask == null)
            {
                Debug.LogWarning($"[ScoreManagerAdapter] Ignoring action '{args.ActionType}' as no task is active.");
                return;
            }

            bool isActionCorrect = (args.ActionType == _currentTask.expectedAction);

            if (isActionCorrect)
            {
                bool compliant = ppeManager.VerifyPPECompliance(_currentTask.TaskData.requiredPPE);

                if (!compliant && !_currentTask.HasMissedPPEOnce)
                {
                    _scoreService.SubtractPoints(
                        _currentTask.TaskData.ppePenalty,
                        "Action correct, but required PPE was missing"
                    );
                    _currentTask.HasMissedPPEOnce = true;
                    _currentTask.State = TaskState.CompletedSuccessButUnsafe;
                }
                else
                {
                    _currentTask.State = TaskState.CompletedSuccess;
                }

                EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs(_currentTask.TaskData, _currentTask));
            }
            else if (!_currentTask.HasFailedOnce)
            {
                _scoreService.SubtractPoints(
                    _currentTask.TaskData.failurePenalty,
                    $"Incorrect action for task '{_currentTask.TaskData.taskName}'"
                );
                _currentTask.HasFailedOnce = true;
            }
        }

        private void HandleScoreChanged(int newScore, int delta, string reason)
        {
            Debug.Log($"[ScoreManagerAdapter] [Score] {reason}: {(delta >= 0 ? "+" : "")}{delta} (Total: {newScore})");
        }
    }
}
