using UnityEngine;

// Forward declare SafetyTask if it's in another file and you need it here

[System.Serializable]
public struct ActionAttemptEventArgs
{
    public ActionType ActionType;
    public int InteractorId; // Could be player ID or specific hand/controller
    public Vector3 WorldPosition;

    public ActionAttemptEventArgs(ActionType actionType, int interactorId, Vector3 worldPosition)
    {
        ActionType = actionType;
        InteractorId = interactorId;
        WorldPosition = worldPosition;
    }
}

[System.Serializable]
public struct PPEStateChangedEventArgs
{
    public PPEType PpeType;
    public bool IsWearing;

    public PPEStateChangedEventArgs(PPEType ppeType, bool isWearing)
    {
        PpeType = ppeType;
        IsWearing = isWearing;
    }
}

[System.Serializable]
public struct TaskEventArgs // Used for TaskStarted, TaskCompleted, TaskTimeout
{
    public SafetyTask Task; // This will be our ScriptableObject type

    public TaskEventArgs(SafetyTask task)
    {
        Task = task;
    }
}

[System.Serializable]
public struct TaskGroupEventArgs // for group‐started / group‐completed
{
    public TaskGroup Group;

    public TaskGroupEventArgs(TaskGroup group)
    {
        Group = group;
    }
}

[System.Serializable]
public struct ScoreChangedEventArgs
{
    public int TotalScore;
    public int Delta;

    public ScoreChangedEventArgs(int totalScore, int delta)
    {
        TotalScore = totalScore;
        Delta = delta;
    }
}

[System.Serializable]
public struct SessionCompletedEventArgs
{
    public float totalElapsedTime;
    public int totalScore;
    public int tasksCompleted;

    public SessionCompletedEventArgs(float totalElapsedTime, int totalScore, int tasksCompleted)
    {
        this.totalElapsedTime = totalElapsedTime;
        this.totalScore = totalScore;
        this.tasksCompleted = tasksCompleted;
    }
}

// Basic session events might not need payloads, but can be structs for consistency
[System.Serializable]
public struct SessionStartedEventArgs { }
[System.Serializable]
public struct SessionPausedEventArgs { }
[System.Serializable]
public struct SessionResumedEventArgs { }
[System.Serializable]
public struct SessionEndedEventArgs { }