using UnityEngine;
using UnityEngine.Events;
using System; // For C# Action

[CreateAssetMenu(fileName = "EventBus", menuName = "VRSafetyTraining/EventBus", order = 0)]
public class EventBus : ScriptableObject
{
    private static EventBus _instance;
    public static EventBus Instance
    {
        get
        {
            if (_instance == null)
            {
                // Attempt to load from Resources first if you place it there.
                // _instance = Resources.Load<EventBus>("EventBus"); // Convention: place in Resources/EventBus.asset
                // If not found, it means it needs to be assigned manually or a different loading strategy.
                // For this project, we'll rely on manual assignment and a FindObjectOfType fallback for editor convenience
                // or a dedicated loader. For now, let's assume it's assigned.
                // If you have multiple EventBus assets, this singleton pattern needs refinement.
                // The spec says "Serialized in Inspector", so direct assignment is key.
                // This static instance is more for C# event access if needed, but primarily UnityEvents will be used.
                Debug.LogError("EventBus instance is null. Ensure an EventBus asset is created and assigned.");
            }
            return _instance;
        }
        // Allow setting instance from a loader or manager if needed.
        set => _instance = value;
    }

    [Header("Debug")]
    public bool verboseLogging = false;

    // --- C# Events (optional, but good for non-MonoBehaviour systems) ---
    public static event Action<SessionStartedEventArgs> OnSessionStarted_CSharp;
    public static event Action<SessionPausedEventArgs> OnSessionPaused_CSharp;
    public static event Action<SessionResumedEventArgs> OnSessionResumed_CSharp;
    public static event Action<SessionEndedEventArgs> OnSessionEnded_CSharp;
    public static event Action<ActionAttemptEventArgs> OnActionAttempt_CSharp;
    public static event Action<PPEStateChangedEventArgs> OnPPEStateChanged_CSharp;
    public static event Action<TaskEventArgs> OnTaskStarted_CSharp;
    public static event Action<TaskEventArgs> OnTaskCompleted_CSharp;
    public static event Action<TaskEventArgs> OnTaskTimeout_CSharp;
    public static event Action<ScoreChangedEventArgs> OnScoreChanged_CSharp;

    // --- UnityEvents (for Inspector assignment) ---
    [Header("Session Events")]
    public UnityEvent<SessionStartedEventArgs> OnSessionStarted;
    public UnityEvent<SessionPausedEventArgs> OnSessionPaused;
    public UnityEvent<SessionResumedEventArgs> OnSessionResumed;
    public UnityEvent<SessionEndedEventArgs> OnSessionEnded;

    [Header("Gameplay Events")]
    public UnityEvent<ActionAttemptEventArgs> OnActionAttempt;
    public UnityEvent<PPEStateChangedEventArgs> OnPPEStateChanged;
    public UnityEvent<TaskEventArgs> OnTaskStarted;
    public UnityEvent<TaskEventArgs> OnTaskCompleted;
    public UnityEvent<TaskEventArgs> OnTaskTimeout; // TaskEventArgs will contain the timed-out task
    public UnityEvent<ScoreChangedEventArgs> OnScoreChanged;

    private void OnEnable()
    {
        // If we want a single instance accessible via static Instance property
        // This ensures that the first loaded EventBus SO becomes the static instance.
        // However, this is tricky with SOs as their lifecycle is different.
        // Better to have a manager load/assign it.
        // For now, we'll just ensure _instance is set if it's null.
        if (_instance == null) _instance = this;
        else if (_instance != this)
        {
            // This can happen if you have multiple EventBus assets loaded or accidentally duplicate one.
            // Decide on a strategy: log error, overwrite, or ignore.
            // Debug.LogWarning($"Multiple EventBus instances. Using the first one loaded: {_instance.name}. Ignoring: {this.name}");
        }
    }


    // --- Methods to Raise Events ---
    public void RaiseSessionStarted(SessionStartedEventArgs args = new SessionStartedEventArgs())
    {
        if (verboseLogging) Debug.Log($"[EventBus] SessionStarted");
        OnSessionStarted_CSharp?.Invoke(args);
        OnSessionStarted?.Invoke(args);
    }

    public void RaiseSessionPaused(SessionPausedEventArgs args = new SessionPausedEventArgs())
    {
        if (verboseLogging) Debug.Log($"[EventBus] SessionPaused");
        OnSessionPaused_CSharp?.Invoke(args);
        OnSessionPaused?.Invoke(args);
    }

    public void RaiseSessionResumed(SessionResumedEventArgs args = new SessionResumedEventArgs())
    {
        if (verboseLogging) Debug.Log($"[EventBus] SessionResumed");
        OnSessionResumed_CSharp?.Invoke(args);
        OnSessionResumed?.Invoke(args);
    }

    public void RaiseSessionEnded(SessionEndedEventArgs args = new SessionEndedEventArgs())
    {
        if (verboseLogging) Debug.Log($"[EventBus] SessionEnded");
        OnSessionEnded_CSharp?.Invoke(args);
        OnSessionEnded?.Invoke(args);
    }

    public void RaiseActionAttempt(ActionAttemptEventArgs args)
    {
        if (verboseLogging) Debug.Log($"[EventBus] ActionAttempt: {args.ActionType}, Interactor: {args.InteractorId}, Pos: {args.WorldPosition}");
        OnActionAttempt_CSharp?.Invoke(args);
        OnActionAttempt?.Invoke(args);
    }

    public void RaisePPEStateChanged(PPEStateChangedEventArgs args)
    {
        if (verboseLogging) Debug.Log($"[EventBus] PPEStateChanged: {args.PpeType}, Wearing: {args.IsWearing}");
        OnPPEStateChanged_CSharp?.Invoke(args);
        OnPPEStateChanged?.Invoke(args);
    }

    public void RaiseTaskStarted(TaskEventArgs args)
    {
        if (verboseLogging) Debug.Log($"[EventBus] TaskStarted: {args.Task.taskName}");
        OnTaskStarted_CSharp?.Invoke(args);
        OnTaskStarted?.Invoke(args);
    }

    public void RaiseTaskCompleted(TaskEventArgs args)
    {
        if (verboseLogging) Debug.Log($"[EventBus] TaskCompleted: {args.Task.taskName}");
        OnTaskCompleted_CSharp?.Invoke(args);
        OnTaskCompleted?.Invoke(args);
    }

    public void RaiseTaskTimeout(TaskEventArgs args)
    {
        if (verboseLogging) Debug.Log($"[EventBus] TaskTimeout: {args.Task.taskName}");
        OnTaskTimeout_CSharp?.Invoke(args);
        OnTaskTimeout?.Invoke(args);
    }

    public void RaiseScoreChanged(ScoreChangedEventArgs args)
    {
        if (verboseLogging) Debug.Log($"[EventBus] ScoreChanged: Total {args.TotalScore}, Delta {args.Delta}");
        OnScoreChanged_CSharp?.Invoke(args);
        OnScoreChanged?.Invoke(args);
    }
}