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
        Completed
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
    public struct TaskEventArgs // Used for TaskStarted, TaskCompleted
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public ISafetyTask Task;
        public RuntimeSafetyTask? RuntimeTask;
        public TaskPhase Phase;

        /// <summary>
        /// Indicates if the worker was wearing all required PPE at the time of completion.
        /// Used when RuntimeTask is null (the emitter does not possess the internal instance).
        /// Default value (true) is ignored when RuntimeTask != null.
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

        public ITaskGroup? Group;
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
        // Stable id of the task that caused the change (empty when not task-driven, e.g. reset or
        // gate penalty) and the human-readable reason (distinguishes completion/ppe-penalty/timeout).
        public string TaskId;
        public string Reason;

        public ScoreChangedEventArgs(int totalScore, int delta)
        {
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;
            TotalScore = totalScore;
            Delta = delta;
            TaskId = string.Empty;
            Reason = string.Empty;
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
        // TaskId/GroupId are stable, language-independent ids (analysis keys in the session log).
        // TaskName/GroupName carry the localized display strings for human-facing surfaces
        // (LogHUD, evaluator dashboard) so those stay readable without parsing the log id.
        public string TaskId;
        public string GroupId;
        public string TaskName;
        public string GroupName;
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

        /// <summary>
        /// Final outcome of every task in the session, in authoring order. Lets the session
        /// log write a per-task block in its summary, so adherence per task (how many
        /// participants skipped the boots) is readable as a table instead of requiring a
        /// replay of each log's event stream. Never null once published by the domain;
        /// empty on payloads built by callers that do not own the task list.
        /// </summary>
        public TaskOutcome[] taskOutcomes;

        public SessionCompletedEventArgs(float totalElapsedTime, int totalScore, int tasksCompleted, int totalTasks)
            : this(totalElapsedTime, totalScore, tasksCompleted, totalTasks, 0, System.Array.Empty<TaskOutcome>())
        {
        }

        public SessionCompletedEventArgs(float totalElapsedTime, int totalScore, int tasksCompleted, int totalTasks, int orderViolationCount)
            : this(totalElapsedTime, totalScore, tasksCompleted, totalTasks, orderViolationCount, System.Array.Empty<TaskOutcome>())
        {
        }

        public SessionCompletedEventArgs(float totalElapsedTime, int totalScore, int tasksCompleted, int totalTasks, int orderViolationCount, TaskOutcome[] taskOutcomes)
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
            this.taskOutcomes = taskOutcomes ?? System.Array.Empty<TaskOutcome>();
        }
    }

    /// <summary>
    /// How a single task ended, with the risk grading it carried at the time. The grades
    /// travel with the outcome on purpose: a session log stays self-describing even if the
    /// scenario's risk matrix is regraded afterwards, so an old log always shows the
    /// weighting that was actually in force when the participant ran it.
    /// </summary>
    [System.Serializable]
    public struct TaskOutcome
    {
        public string TaskId;
        public string TaskName;
        public string GroupId;
        public string GroupName;
        public TaskState State;
        public RiskAssessment Risk;

        /// <summary>Seconds into the session when the task reached its terminal state.</summary>
        public float CompletionTime;
    }

    [System.Serializable]
    public struct SessionStartedEventArgs
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        /// <summary>Total number of tasks in the loaded scenario. Lets the session logger report an
        /// accurate denominator even when the session is abandoned before SessionCompleted fires.</summary>
        public int TotalTasks;
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
