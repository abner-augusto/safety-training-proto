using UnityEngine;
using TMPro;
using System.Collections;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TimerUI : MonoBehaviour
{
    [Tooltip("Assign your EventBus ScriptableObject asset here. Can be auto-found.")]
    public EventBus eventBus;
    private TextMeshProUGUI _timerText;
    private Coroutine _countdownCoroutine;
    private float _timeRemaining;

    void Start()
    {
        _timerText = GetComponent<TextMeshProUGUI>();
        if (eventBus == null)
        {
            eventBus = EventBus.Instance;
            if (eventBus == null)
            {
                Debug.LogError("EventBus not assigned or found for TimerUI!", this);
                enabled = false;
                return;
            }
        }

        eventBus.OnTaskStarted.AddListener(HandleTaskStarted);
        eventBus.OnTaskCompleted.AddListener(HandleTaskEnded); // Task ended (completed or timed out)
        eventBus.OnTaskTimeout.AddListener(HandleTaskEnded);

        _timerText.text = "Timer: --:--";
    }

    private void OnDestroy()
    {
        if (eventBus != null)
        {
            eventBus.OnTaskStarted.RemoveListener(HandleTaskStarted);
            eventBus.OnTaskCompleted.RemoveListener(HandleTaskEnded);
            eventBus.OnTaskTimeout.RemoveListener(HandleTaskEnded);
        }
        StopCurrentCountdown();
    }

    private void HandleTaskStarted(TaskEventArgs args)
    {
        StopCurrentCountdown();
        if (args.Task.timeLimit > 0)
        {
            _timeRemaining = args.Task.timeLimit;
            _countdownCoroutine = StartCoroutine(CountdownRoutine());
            _timerText.color = Color.white; // Reset color
        }
        else
        {
            _timerText.text = "Timer: N/A";
        }
    }

    private void HandleTaskEnded(TaskEventArgs args) // Handles both completion and timeout
    {
        StopCurrentCountdown();
        // Check if the event is for the task this UI was tracking (if we were tracking one)
        if (_timeRemaining > 0 && args.Task.timeLimit > 0) // If there was a timed task
        {
            // If task timed out, show 0. If completed, it stops where it was.
            // The TimerSystem's TaskTimeout event is the definitive timeout signal.
            bool timedOut = eventBus.OnTaskTimeout.GetPersistentEventCount() > 0 &&
                            eventBus.OnTaskTimeout.GetPersistentTarget(0) != null; // This is a hacky way to check if the event came from timeout
            // A better way: TaskEventArgs could include a status.
            // For now, let's assume if it's timeout, text reflects that.
            // Actually, the event payload from TaskTimeout is what matters.
            // We need a flag or state.

            // If the event is specifically TaskTimeout, then mark as timed out
            // This requires knowing which event triggered this handler.
            // Simplification: TimerSystem already sent TaskTimeoutEvent. If this TimerUI receives it,
            // it means time is up.
            if (args.Task.timeLimit > 0 && _timeRemaining <= 0.01f) // Effectively timed out by its own coroutine or external event
            {
                _timerText.text = "Time Up!";
                _timerText.color = Color.red;
            }
            else if (args.Task.timeLimit > 0)
            {
                _timerText.text = $"Completed!"; // Or keep the final time
                _timerText.color = Color.green;
            }
        }
    }

    private IEnumerator CountdownRoutine()
    {
        while (_timeRemaining > 0)
        {
            _timeRemaining -= Time.deltaTime;
            if (_timeRemaining < 0) _timeRemaining = 0;

            int minutes = Mathf.FloorToInt(_timeRemaining / 60F);
            int seconds = Mathf.FloorToInt(_timeRemaining - minutes * 60);
            _timerText.text = $"Time: {minutes:00}:{seconds:00}";
            yield return null;
        }
        // Countdown finished internally
        _timeRemaining = 0; // Ensure it's exactly 0
        _timerText.text = "Time Up!";
        _timerText.color = Color.red;
        _countdownCoroutine = null;
        // The TimerSystem will raise the TaskTimeoutEvent. This UI just reflects.
    }

    private void StopCurrentCountdown()
    {
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
    }
}