using UnityEngine;
using System.Collections.Generic; // For List in SafetyTaskArgs

// Forward declare SafetyTask if it's in another file and you need it here
// Or define it later and then come back to reference it
// For now, we'll assume SafetyTask will be defined elsewhere.

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

// We need SafetyTask defined before this, so let's placeholder it
// Or define SafetyTask SO first. For now, let's use a placeholder name.
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

// Basic session events might not need payloads, but can be structs for consistency
[System.Serializable]
public struct SessionStartedEventArgs { }
[System.Serializable]
public struct SessionPausedEventArgs { }
[System.Serializable]
public struct SessionResumedEventArgs { }
[System.Serializable]
public struct SessionEndedEventArgs { }