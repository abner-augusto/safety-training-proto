#nullable enable
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Core
{
    public enum TaskGroupPhase
    {
        Started,
        Completed
    }

    public enum TaskPhase
    {
        Started,
        Completed,
        Timeout
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
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;
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

        public ISafetyTask Task;                  // was SafetyTask
        public RuntimeSafetyTask? RuntimeTask;
        public TaskPhase Phase;

        /// <summary>
        /// Indicates if the worker was wearing all required PPE at the time of completion.
        /// Used when RuntimeTask is null (the emitter does not possess the internal instance).
        /// Default value (false) is ignored when RuntimeTask != null.
        /// </summary>
        public bool WasPpeCompliant;

        public TaskEventArgs(ISafetyTask task, RuntimeSafetyTask? runtimeTask = null)
        {
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;
            Task = task;
            RuntimeTask = runtimeTask;
            Phase = TaskPhase.Started;
            WasPpeCompliant = true;
        }

        public TaskEventArgs(ISafetyTask task, RuntimeSafetyTask? runtimeTask, TaskPhase phase)
        {
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;
            Task = task;
            RuntimeTask = runtimeTask;
            Phase = phase;
            WasPpeCompliant = true;
        }
    }

    [System.Serializable]
    public struct TaskGroupEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public ITaskGroup? Group;                  // was TaskGroup
        public TaskGroupPhase Phase;

        public TaskGroupEventArgs(ITaskGroup? group)
        {
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;
            Group = group;
            Phase = TaskGroupPhase.Started;
        }

        public TaskGroupEventArgs(ITaskGroup? group, TaskGroupPhase phase)
        {
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;
            Group = group;
            Phase = phase;
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
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;
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
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;
            this.totalElapsedTime = totalElapsedTime;
            this.totalScore = totalScore;
            this.tasksCompleted = tasksCompleted;
            this.totalTasks = totalTasks;
            orderViolationCount = 0;
        }

        public SessionCompletedEventArgs(float totalElapsedTime, int totalScore, int tasksCompleted, int totalTasks, int orderViolationCount)
        {
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;
            this.totalElapsedTime = totalElapsedTime;
            this.totalScore = totalScore;
            this.tasksCompleted = tasksCompleted;
            this.totalTasks = totalTasks;
            this.orderViolationCount = orderViolationCount;
        }
    }

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
