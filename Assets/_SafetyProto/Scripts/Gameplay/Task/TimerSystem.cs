using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class TimerSystem : MonoBehaviour
{
    [Tooltip("Assign your TaskManager here so we can tell which group is active.")]
    public TaskManager taskManager;
    [Header("Events")]
    public UnityEvent<float> onTimeUpdated = new UnityEvent<float>(); // (remaining time)
    public UnityEvent<float> onTimerCompleted = new UnityEvent<float>(); // (elapsed time)
    public UnityEvent onTimerTimeout = new UnityEvent();

    private Coroutine _currentTimerCoroutine;
    private TaskGroup _timedGroup;
    private float _timeRemaining;
    private float _elapsedTime;
    private bool _isPaused;

    void Start()
    {
        if (EventBus.Instance == null)
        {
            Debug.LogError("TimerSystem: EventBus not assigned!", this);
            enabled = false;
            return;
        }

        if (taskManager == null)
        {
            taskManager = FindFirstObjectByType<TaskManager>();
            if (taskManager == null)
            {
                Debug.LogError("TimerSystem: No TaskManager found in scene!", this);
                enabled = false;
                return;
            }
        }

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

    private void OnTaskStartedForFreeOrder(TaskEventArgs args)
    {
        // If no group is currently being timed, but we're inside a FreeOrder group, kick off the timer:
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
        if (_currentTimerCoroutine != null)
        {
            StopCurrentTimer();
        }

        _timedGroup = args.Group;
        _timeRemaining = _timedGroup.timeLimit;
        _elapsedTime = 0f;
        _isPaused = false;
        if (_timedGroup.timeLimit > 0)
        {
            _currentTimerCoroutine = StartCoroutine(GroupCountdownRoutine(_timedGroup.timeLimit));
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
        if (_currentTimerCoroutine != null)
        {
            StopCoroutine(_currentTimerCoroutine);
            _currentTimerCoroutine = null;
        }
    }

    private IEnumerator GroupCountdownRoutine(float duration)
    {
        _timeRemaining = duration;
        _elapsedTime = 0f;
        while (_timeRemaining > 0)
        {
            if (!_isPaused)
            {
                _timeRemaining -= Time.deltaTime;
                _elapsedTime += Time.deltaTime;
                if (_timeRemaining < 0) _timeRemaining = 0;
                onTimeUpdated.Invoke(_timeRemaining);
            }
            yield return null;
        }

        onTimeUpdated.Invoke(0);
        _currentTimerCoroutine = null;
        Debug.Log("TimerSystem: Group timer reached zero → timing out.");
        if (EventBus.Instance != null && _timedGroup != null)
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
}