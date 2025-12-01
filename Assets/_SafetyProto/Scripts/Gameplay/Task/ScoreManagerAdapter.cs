using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.Task
{
    public class ScoreManagerAdapter : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("ScriptableObject holding the shared scoring service.")]
        public ScoreServiceSO scoreServiceAsset;

        private IScoreService _scoreService;

        private void Awake()
        {
            if (scoreServiceAsset == null)
            {
                Debug.LogError("ScoreManagerAdapter is missing required references (ScoreService).", this);
                SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                {
                    Source = nameof(ScoreManagerAdapter),
                    Message = "ScoreService asset missing",
                    Details = $"ScoreManagerAdapter on '{name}' requires a ScoreServiceSO reference."
                });
                enabled = false;
                return;
            }
            _scoreService = scoreServiceAsset.Service;
        }

        private void OnEnable()
        {
            if (_scoreService == null || EventBus.Instance == null || !this.IsEventBusReady())
                return;

            EventBus.Instance.onTaskCompleted.AddListener(HandleTaskCompleted);
            EventBus.Instance.onTaskTimeout.AddListener(HandleTaskTimeout);
            _scoreService.ScoreChanged += HandleScoreChanged;
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onTaskCompleted.RemoveListener(HandleTaskCompleted);
                EventBus.Instance.onTaskTimeout.RemoveListener(HandleTaskTimeout);
            }

            if (_scoreService != null)
                _scoreService.ScoreChanged -= HandleScoreChanged;
        }

        private void HandleTaskCompleted(TaskEventArgs args)
        {
            if (args.Task == null) return;

            _scoreService.AddPoints(args.Task.successPoints, $"Task '{args.Task.taskName}' completed");

            if (args.RuntimeTask != null && args.RuntimeTask.State == TaskState.CompletedSuccessButUnsafe)
            {
                int penalty = args.Task.ppePenalty;
                if (penalty > 0)
                {
                    _scoreService.SubtractPoints(penalty, $"Safety Violation: Missing PPE during '{args.Task.taskName}'");
                }
            }
        }

        private void HandleTaskTimeout(TaskEventArgs args)
        {
            if (args.Task != null)
                _scoreService.SubtractPoints(args.Task.failurePenalty, $"Task '{args.Task.taskName}' timed out");
        }

        private void HandleScoreChanged(int newScore, int delta, string reason)
        {
            Debug.Log($"[ScoreManagerAdapter] [Score] {reason}: {(delta >= 0 ? "+" : "")}{delta} (Total: {newScore})");
            ScoreEvents.RaiseScoreChanged(new ScoreChangedEventArgs(newScore, delta));
        }
    }
}
