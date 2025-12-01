using System.Threading;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Utils;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Gameplay.Task
{
    public class TimerSystem : MonoBehaviour, ISessionResettable
    {
        [Tooltip("Assign your TaskManager here so we can tell which group is active.")]
        public TaskManager taskManager;

        [Header("Events")]
        public UnityEvent<float> onTimeUpdated = new UnityEvent<float>();
        public UnityEvent<float> onTimerCompleted = new UnityEvent<float>();
        public UnityEvent onTimerTimeout = new UnityEvent();

        private CancellationTokenSource _timerCts;
        private TaskGroup _timedGroup;
        private float _timeRemaining;
        private float _elapsedTime;
        private float _sessionStartTime = -1f;
        private bool _isPaused;

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            if (taskManager == null)
            {
                taskManager = FindFirstObjectByType<TaskManager>();
                if (taskManager == null)
                {
                    Debug.LogError("TimerSystem: No TaskManager found in scene!", this);
                    SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                    {
                        Source = nameof(TimerSystem),
                        Message = "TaskManager missing",
                        Details = "TimerSystem requires a TaskManager reference to operate."
                    });
                    enabled = false;
                    return;
                }
            }

            EventBus.Instance.onSessionStarted.AddListener(OnSessionStarted);
            EventBus.Instance.onGroupStarted.AddListener(OnGroupStarted);
            EventBus.Instance.onGroupCompleted.AddListener(OnGroupCompleted);
            EventBus.Instance.onTaskStarted.AddListener(OnTaskStartedForFreeOrder);
            EventBus.Instance.onSessionPaused.AddListener(PauseTimer);
            EventBus.Instance.onSessionResumed.AddListener(ResumeTimer);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onGroupStarted.RemoveListener(OnGroupStarted);
                EventBus.Instance.onGroupCompleted.RemoveListener(OnGroupCompleted);
                EventBus.Instance.onTaskStarted.RemoveListener(OnTaskStartedForFreeOrder);
                EventBus.Instance.onSessionPaused.RemoveListener(PauseTimer);
                EventBus.Instance.onSessionResumed.RemoveListener(ResumeTimer);
            }

            StopCurrentTimer();
        }

        private void OnSessionStarted(SessionStartedEventArgs _)
        {
            _sessionStartTime = Time.time;
        }

        private void OnTaskStartedForFreeOrder(TaskEventArgs args)
        {
            if (_timedGroup == null)
            {
                TaskGroup current = taskManager.GetCurrentGroup();
                if (current != null &&
                    current.executionMode == TaskExecutionMode.FreeOrder &&
                    current.tasks.Contains(args.Task))
                {
                    StartTimerForGroup(new TaskGroupEventArgs(current));
                }
            }
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            var group = args.Group;
            if (group == null) return;

            if (group.executionMode == TaskExecutionMode.Sequential)
            {
                StartTimerForGroup(args);
            }
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            var group = args.Group;
            if (group == null) return;

            if (_timedGroup == group)
            {
                StopCurrentTimer();
                _timedGroup = null;
            }
        }

        private void StartTimerForGroup(TaskGroupEventArgs args)
        {
            StopCurrentTimer();

            _timedGroup = args.Group;
            _timeRemaining = _timedGroup.timeLimit;
            _elapsedTime = 0f;
            _isPaused = false;
            if (_timedGroup.timeLimit > 0)
            {
                _timerCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
                _ = GroupCountdownRoutine(_timedGroup.timeLimit, _timerCts.Token);
                onTimeUpdated.Invoke(_timedGroup.timeLimit);
                Debug.Log($"TimerSystem: Started timer for group '{_timedGroup.groupName}' ({_timedGroup.timeLimit}s).");
            }
            else
            {
                onTimeUpdated.Invoke(0);
                Debug.Log($"TimerSystem: Group '{_timedGroup.groupName}' has no time limit.");
            }
        }

        private void StopCurrentTimer()
        {
            if (_timerCts != null)
            {
                _timerCts.Cancel();
                _timerCts.Dispose();
                _timerCts = null;
            }
        }

        private async Awaitable GroupCountdownRoutine(float duration, CancellationToken token)
        {
            _timeRemaining = duration;
            _elapsedTime = 0f;
            while (_timeRemaining > 0)
            {
                if (token.IsCancellationRequested || this == null)
                {
                    return;
                }

                if (!_isPaused)
                {
                    _timeRemaining -= Time.deltaTime;
                    _elapsedTime += Time.deltaTime;
                    if (_timeRemaining < 0) _timeRemaining = 0;
                    onTimeUpdated.Invoke(_timeRemaining);
                }

                await Awaitable.NextFrameAsync(token);
            }

            onTimeUpdated.Invoke(0);
            _timerCts = null;
            if (EventBus.Instance != null && _timedGroup != null && !token.IsCancellationRequested && this != null)
            {
                onTimerTimeout.Invoke();
            }
        }

        private void PauseTimer(SessionPausedEventArgs _)
        {
            _isPaused = true;
        }

        private void ResumeTimer(SessionResumedEventArgs _)
        {
            _isPaused = false;
        }

        public float GetTimeRemaining() => _timeRemaining;
        public float GetElapsedTime() => _elapsedTime;
        public float GetTotalSessionTime()
        {
            if (_sessionStartTime < 0) return 0f;
            return Time.time - _sessionStartTime;
        }

        public void ResetSession()
        {
            StopCurrentTimer();
            _timedGroup = null;
            _timeRemaining = 0f;
            _elapsedTime = 0f;
            onTimeUpdated.Invoke(0f);
        }
    }
}
