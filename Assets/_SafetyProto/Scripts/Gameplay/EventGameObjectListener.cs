using UnityEngine;

public class EventGameObjectToggleListener : MonoBehaviour
{
    public enum EventType
    {
        SessionStarted,
        SessionPaused,
        SessionResumed,
        SessionEnded,
        TaskStarted,
        TaskCompleted,
        AllTasksCompleted,
        TaskTimeout,
        ScoreChanged,
        PpeStateChanged,
        ActionAttempt
    }

    [Header("Configuration")]
    public EventBus eventBus;
    public EventType eventTypeToListen;
    public GameObject target;
    public bool enableOnEvent = true;

    void Start()
    {
        if (eventBus == null || target == null)
        {
            Debug.LogError("Missing references in EventGameObjectToggleListener", this);
            enabled = false;
            return;
        }

        switch (eventTypeToListen)
        {
            case EventType.SessionStarted:
                eventBus.onSessionStarted.AddListener(_ => Toggle());
                break;
            case EventType.SessionPaused:
                eventBus.onSessionPaused.AddListener(_ => Toggle());
                break;
            case EventType.SessionResumed:
                eventBus.onSessionResumed.AddListener(_ => Toggle());
                break;
            case EventType.SessionEnded:
                eventBus.onSessionEnded.AddListener(_ => Toggle());
                break;
            case EventType.TaskStarted:
                eventBus.onTaskStarted.AddListener(_ => Toggle());
                break;
            case EventType.TaskCompleted:
                eventBus.onTaskCompleted.AddListener(_ => Toggle());
                break;
            case EventType.TaskTimeout:
                eventBus.onTaskTimeout.AddListener(_ => Toggle());
                break;
            case EventType.ScoreChanged:
                eventBus.onScoreChanged.AddListener(_ => Toggle());
                break;
            case EventType.PpeStateChanged:
                eventBus.onPpeStateChanged.AddListener(_ => Toggle());
                break;
            case EventType.ActionAttempt:
                eventBus.onActionAttempt.AddListener(_ => Toggle());
                break;
            case EventType.AllTasksCompleted:
                eventBus.onAllTasksCompleted.AddListener(Toggle);
                break;
        }
    }

    void Toggle()
    {
        target.SetActive(enableOnEvent);
    }
    
}