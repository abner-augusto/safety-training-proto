using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.Task;
using UnityEngine;

namespace SafetyProto.Core
{
    [System.Serializable]
    public struct ActionAttemptEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

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
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

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
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public SafetyTask Task; // Base task data
        public RuntimeSafetyTask RuntimeTask; // Runtime instance when available

        public TaskEventArgs(SafetyTask task, RuntimeSafetyTask runtimeTask = null)
        {
            Task = task;
            RuntimeTask = runtimeTask;
        }
    }

    [System.Serializable]
    public struct TaskGroupEventArgs // for group-started / group-completed
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public TaskGroup Group;

        public TaskGroupEventArgs(TaskGroup group)
        {
            Group = group;
        }
    }

    [System.Serializable]
    public struct ScoreChangedEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public int TotalScore;
        public int Delta;

        public ScoreChangedEventArgs(int totalScore, int delta)
        {
            TotalScore = totalScore;
            Delta = delta;
        }
    }

    [System.Serializable]
    public struct SafetyViolationEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public string ViolationCode;
        public string Message;
        public string TaskId;
        public string GroupId;
    }

    [System.Serializable]
    public struct CriticalSafetyFailureEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;
        public int ViolationCount;
        public float WindowSeconds;
        public string Reason;
    }

    [System.Serializable]
    public struct SafetyErrorEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;
        public string Source;
        public string Message;
        public string Details;
    }

    [System.Serializable]
    public struct SessionCompletedEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public float totalElapsedTime;
        public int totalScore;
        public int tasksCompleted;
        public int totalTasks;
        public int orderViolationCount;

        public SessionCompletedEventArgs(float totalElapsedTime, int totalScore, int tasksCompleted, int totalTasks)
        {
            this.totalElapsedTime = totalElapsedTime;
            this.totalScore = totalScore;
            this.tasksCompleted = tasksCompleted;
            this.totalTasks = totalTasks;
            orderViolationCount = 0;
        }

        public SessionCompletedEventArgs(float totalElapsedTime, int totalScore, int tasksCompleted, int totalTasks, int orderViolationCount)
        {
            this.totalElapsedTime = totalElapsedTime;
            this.totalScore = totalScore;
            this.tasksCompleted = tasksCompleted;
            this.totalTasks = totalTasks;
            this.orderViolationCount = orderViolationCount;
        }
    }

    // Basic session events might not need payloads, but can be structs for consistency
    [System.Serializable]
    public struct SessionStartedEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;
    }
    [System.Serializable]
    public struct SessionPausedEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;
    }
    [System.Serializable]
    public struct SessionResumedEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;
    }
    [System.Serializable]
    public struct SessionEndedEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;
    }
}
