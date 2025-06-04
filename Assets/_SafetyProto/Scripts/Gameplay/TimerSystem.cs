using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class TimerSystem : MonoBehaviour
{
    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;

    [Header("Events")]
    public UnityEvent<float> onTimeUpdated = new UnityEvent<float>();
    public UnityEvent<float> onTimerCompleted = new UnityEvent<float>();  // Pass elapsed time
    public UnityEvent onTimerTimeout = new UnityEvent();

    private Coroutine _currentTimerCoroutine;
    private SafetyTask _timedTask;
    private float _timeRemaining;
    private float _elapsedTime;
    private bool _isPaused;

    void Start()
    {
        Debug.Log($"TimerSystem: Subscribing to eventBus={eventBus?.name}");
        if (eventBus == null)
        {
            Debug.LogError("EventBus not assigned to TimerSystem!", this);
            enabled = false;
            return;
        }
        eventBus.onTaskStarted.AddListener(StartTimerForTask);
        // Add debug here to check listener count
        Debug.Log($"TimerSystem: Now listening to onTaskStarted. Listeners: {eventBus.onTaskStarted.GetPersistentEventCount()}");

        eventBus.onTaskStarted.AddListener(StartTimerForTask);
        eventBus.onTaskCompleted.AddListener(StopTimerForTask); // Early finish
        eventBus.onTaskTimeout.AddListener(StopTimerForTask);   // Timeout finish
        eventBus.onSessionPaused.AddListener(PauseTimer);
        eventBus.onSessionResumed.AddListener(ResumeTimer);
    }

    private void OnDestroy()
    {
        if (eventBus != null)
        {
            eventBus.onTaskStarted.RemoveListener(StartTimerForTask);
            eventBus.onTaskCompleted.RemoveListener(StopTimerForTask);
            eventBus.onTaskTimeout.RemoveListener(StopTimerForTask);
            eventBus.onSessionPaused.RemoveListener(PauseTimer);
            eventBus.onSessionResumed.RemoveListener(ResumeTimer);
        }
        StopCurrentTimer();
    }

    private void StartTimerForTask(TaskEventArgs args)
    {
        Debug.Log($"TimerSystem: Received onTaskStarted for '{args.Task.taskName}', timeLimit={args.Task.timeLimit}");
        StopCurrentTimer(); // Stop any existing timer

        _timedTask = args.Task;
        _timeRemaining = _timedTask.timeLimit;
        _elapsedTime = 0f;
        _isPaused = false;

        if (_timedTask.timeLimit > 0)
        {
            _currentTimerCoroutine = StartCoroutine(TaskCountdownRoutine(_timedTask.timeLimit));
            onTimeUpdated.Invoke(_timedTask.timeLimit); // Initial update
            Debug.Log($"TimerSystem: Started timer for task '{_timedTask.taskName}' ({_timedTask.timeLimit}s).");
        }
        else
        {
            onTimeUpdated.Invoke(0);
            Debug.Log($"TimerSystem: Task '{_timedTask.taskName}' has no time limit.");
        }
    }

    private void StopTimerForTask(TaskEventArgs args)
    {
        if (_timedTask != null && args.Task == _timedTask)
        {
            StopCurrentTimer();
            if (_timeRemaining > 0) // Completed before timeout
            {
                onTimerCompleted.Invoke(_elapsedTime);
            }
            _timedTask = null;
        }
    }

    private IEnumerator TaskCountdownRoutine(float duration)
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
        Debug.Log("TimerSystem: Timer reached zero, task timed out.");
        if (eventBus != null && _timedTask != null)
            eventBus.RaiseTaskTimeout(new TaskEventArgs(_timedTask));
        onTimerTimeout.Invoke();
        _timedTask = null;
    }

    private void StopCurrentTimer()
    {
        if (_currentTimerCoroutine != null)
        {
            StopCoroutine(_currentTimerCoroutine);
            _currentTimerCoroutine = null;
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
