using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class TimerSystem : MonoBehaviour
{
    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;

    [Tooltip("Assign your TaskManager here so we can tell which group is active.")]
    public TaskManager taskManager;

    [Header("Events")]
    public UnityEvent<float> onTimeUpdated = new UnityEvent<float>();  // (remaining time)
    public UnityEvent<float> onTimerCompleted = new UnityEvent<float>(); // (elapsed time)
    public UnityEvent onTimerTimeout = new UnityEvent();

    private Coroutine _currentTimerCoroutine;
    private TaskGroup _timedGroup;
    private float _timeRemaining;
    private float _elapsedTime;
    private bool _isPaused;

    void Start()
    {
        if (eventBus == null)
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

        // Listen for group‐started / group‐completed
        eventBus.onGroupStarted.AddListener(OnGroupStarted);
        eventBus.onGroupCompleted.AddListener(OnGroupCompleted);

        // Also listen to onTaskStarted so that a FREE‐ORDER group only actually begins its timer
        // once the very first task in that group has fired onTaskStarted.
        eventBus.onTaskStarted.AddListener(OnTaskStartedForFreeOrder);
        eventBus.onSessionPaused.AddListener(PauseTimer);
        eventBus.onSessionResumed.AddListener(ResumeTimer);
    }

    private void OnDestroy()
    {
        if (eventBus != null)
        {
            eventBus.onGroupStarted.RemoveListener(OnGroupStarted);
            eventBus.onGroupCompleted.RemoveListener(OnGroupCompleted);
            eventBus.onTaskStarted.RemoveListener(OnTaskStartedForFreeOrder);
            eventBus.onSessionPaused.RemoveListener(PauseTimer);
            eventBus.onSessionResumed.RemoveListener(ResumeTimer);
        }
        StopCurrentTimer();
    }

    // Called when ANY task starts. We only care if it's the FIRST task in a FREE‐ORDER group.
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

    // Called when a new group begins (sequential or free order) → possibly start its timer immediately (if sequential)
    private void OnGroupStarted(TaskGroupEventArgs args)
    {
        var group = args.Group;
        if (group == null) return;

        // If it's SEQUENTIAL, start immediately:
        if (group.executionMode == TaskExecutionMode.Sequential)
        {
            StartTimerForGroup(args);
        }
        else
        {
            // Free‐order: delay starting until first TaskStartedForFreeOrder
            // i.e. do nothing here except record that _timedGroup should be started later.
            // But we still store it in case OnTaskStartedForFreeOrder triggers:
            // (we actually do that inside OnTaskStartedForFreeOrder logic).
        }
    }

    // Called when *that* group finishes (either sequential or free‐order).
    private void OnGroupCompleted(TaskGroupEventArgs args)
    {
        var group = args.Group;
        if (group == null) return;

        // If this is the group we are timing, stop it now.
        if (_timedGroup == group)
        {
            StopCurrentTimer();
            _timedGroup = null;
        }
    }

    // Actually begins the countdown for a given TaskGroup
    private void StartTimerForGroup(TaskGroupEventArgs args)
    {
        // If some other group was already being timed, stop it first (shouldn't normally happen).
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
            onTimeUpdated.Invoke(_timedGroup.timeLimit); // initial UI update
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
        if (eventBus != null && _timedGroup != null)
        {
            // Fire a timeout event (the group has timed out; you can hook this to UI or score penalty)
            onTimerTimeout.Invoke();
        }

        // We do NOT automatically advance the group here—TaskManager is already responsible for advancing
        // when tasks finish or timeouts occur on individual tasks. If you want a full‐group timeout to force
        // group completion, you’d call eventBus.RaiseGroupCompleted(_timedGroup) here as well.
        // e.g.: eventBus.RaiseGroupCompleted(new TaskGroupEventArgs(_timedGroup));
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
