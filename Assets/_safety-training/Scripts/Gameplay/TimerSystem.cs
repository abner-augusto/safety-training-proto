using UnityEngine;
using System.Collections;

public class TimerSystem : MonoBehaviour
{
    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;
    // No need for TaskManager reference, it listens to TaskManager events

    private Coroutine _currentTimerCoroutine;
    private SafetyTask _timedTask; // The task currently being timed

    void Start()
    {
        if (eventBus == null)
        {
            Debug.LogError("EventBus not assigned to TimerSystem!", this);
            enabled = false;
            return;
        }

        eventBus.OnTaskStarted.AddListener(StartTimerForTask);
        eventBus.OnTaskCompleted.AddListener(StopTimerForTask); // Stop if completed early
        // TaskManager will also call HandleTaskTimeout, which will stop the timer too.
    }

    private void OnDestroy()
    {
        if (eventBus != null)
        {
            eventBus.OnTaskStarted.RemoveListener(StartTimerForTask);
            eventBus.OnTaskCompleted.RemoveListener(StopTimerForTask);
        }
        StopCurrentTimer(); // Ensure coroutine is stopped if object is destroyed
    }

    private void StartTimerForTask(TaskEventArgs args)
    {
        StopCurrentTimer(); // Stop any existing timer

        _timedTask = args.Task;
        if (_timedTask.timeLimit > 0)
        {
            _currentTimerCoroutine = StartCoroutine(TaskCountdownRoutine(_timedTask));
            Debug.Log($"TimerSystem: Started timer for task '{_timedTask.taskName}' ({_timedTask.timeLimit}s).");
        }
        else
        {
            Debug.Log($"TimerSystem: Task '{_timedTask.taskName}' has no time limit.");
        }
    }

    private void StopTimerForTask(TaskEventArgs args)
    {
        // Only stop if the completed/timed-out task is the one we are currently timing
        if (_timedTask != null && args.Task == _timedTask)
        {
            StopCurrentTimer();
            Debug.Log($"TimerSystem: Stopped timer for task '{args.Task.taskName}' due to completion/external stop.");
            _timedTask = null; // Clear the timed task
        }
    }


    private IEnumerator TaskCountdownRoutine(SafetyTask task)
    {
        yield return new WaitForSeconds(task.timeLimit);

        // Check if this task is still the one we should be timing
        // (e.g., it wasn't completed early and a new one started)
        if (_timedTask == task)
        {
            Debug.Log($"TimerSystem: Task '{task.taskName}' timed out.");
            eventBus.RaiseTaskTimeout(new TaskEventArgs(task));
            _timedTask = null; // Clear the timed task as it has now officially timed out
            _currentTimerCoroutine = null;
        }
    }

    private void StopCurrentTimer()
    {
        if (_currentTimerCoroutine != null)
        {
            StopCoroutine(_currentTimerCoroutine);
            _currentTimerCoroutine = null;
            // Debug.Log("TimerSystem: Current timer coroutine stopped.");
        }
    }
}