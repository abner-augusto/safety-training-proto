using UnityEngine;
using System; // Needed for Action

// Manages a countdown timer for the current task
public class TimerSystem : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static TimerSystem Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }
    // --- End Singleton Pattern ---

    private float currentTime = 0f;
    private float duration = 0f;
    private bool isRunning = false;
    private bool hasTimedOut = false; // Flag to prevent multiple timeout events

    public float RemainingTime => Mathf.Max(0f, duration - currentTime);
    public bool IsRunning => isRunning;

    // Event emitted when the timer reaches zero
    public event Action OnTaskTimeout;

    // Optional: Event to notify UI frequently (less efficient than UI polling but event-driven)
    // public event Action<float> OnTimerUpdated;

    void Update()
    {
        if (isRunning)
        {
            currentTime += Time.deltaTime;

            // OnTimerUpdated?.Invoke(RemainingTime); // Optional: Fire update event

            if (currentTime >= duration && !hasTimedOut)
            {
                hasTimedOut = true; // Set flag immediately
                Debug.Log("TimerSystem: Task timed out!");
                StopTimer();
                OnTaskTimeout?.Invoke(); // Emit timeout event
            }
        }
    }

    public void StartTimer(float taskDuration)
    {
        duration = taskDuration;
        currentTime = 0f;
        isRunning = true;
        hasTimedOut = false; // Reset timeout flag
        Debug.Log($"TimerSystem: Timer started for {duration} seconds.");
    }

    public void StopTimer()
    {
        isRunning = false;
        Debug.Log("TimerSystem: Timer stopped.");
    }

    public void ResetTimer()
    {
        StopTimer();
        currentTime = 0f;
        duration = 0f;
        hasTimedOut = false;
    }
}