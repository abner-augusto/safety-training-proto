#nullable enable
using SafetyProto.Core.Events;
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
    public struct PPEStateChangedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

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
    public struct TaskEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        public ISafetyTask Task;
        [System.NonSerialized] public RuntimeSafetyTask? RuntimeTask;
        public TaskPhase Phase;

        /// <summary>
        /// Indicates if the worker was wearing all required PPE at the time of completion.
        /// Used when RuntimeTask is null (the emitter does not possess the internal instance).
        /// Default value (true) is ignored when RuntimeTask != null.
        /// </summary>
        public bool WasPpeCompliant;

        /// <summary>
        /// The task's final state: the runtime instance's when the emitter owns one, otherwise
        /// derived from <see cref="WasPpeCompliant"/>. Consumers read this, never the two sources.
        /// </summary>
        public readonly TaskState Outcome =>
            RuntimeTask?.State
            ?? (WasPpeCompliant ? TaskState.CompletedSuccess : TaskState.CompletedSuccessButUnsafe);

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
    public struct TaskGroupEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

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
    public struct ScoreChangedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        public int TotalScore;
        public int Delta;
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
    public struct SafetyViolationEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        public string ViolationCode;
        public string Message;
        public string TaskId;
        public string GroupId;
        public string TaskName;
        public string GroupName;
    }

    [System.Serializable]
    public struct CriticalSafetyFailureEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        public int ViolationCount;
        public float WindowSeconds;
        public string Reason;
    }

    [System.Serializable]
    public struct SafetyErrorEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        public string Source;
        public string Message;
        public string Details;
    }

    /// <summary>What happened between the physical room and the virtual floor.</summary>
    public enum PlayspaceNoticeKind
    {
        /// <summary>The participant's body left the walkable floor (e.g. past the scaffold plank).</summary>
        LeftWalkableArea,
        /// <summary>The participant came back; <see cref="PlayspaceNoticeEventArgs.DurationSeconds"/> says after how long.</summary>
        ReturnedToWalkableArea,
        /// <summary>A system recenter (Meta button) discarded the room-center calibration.</summary>
        RoomCenterDiscarded,
    }

    /// <summary>
    /// XR ergonomics notice: the physical play area and the virtual floor disagree. Logged and
    /// shown to the evaluator, never scored — it reflects the room and tracking drift, not a
    /// decision the participant made in the scenario.
    /// </summary>
    [System.Serializable]
    public struct PlayspaceNoticeEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        public PlayspaceNoticeKind Kind;
        /// <summary>Walkable area concerned, e.g. "andaime"; empty for <see cref="PlayspaceNoticeKind.RoomCenterDiscarded"/>.</summary>
        public string Area;
        /// <summary>Seconds spent outside; set on <see cref="PlayspaceNoticeKind.ReturnedToWalkableArea"/> only.</summary>
        public float DurationSeconds;
    }

    [System.Serializable]
    public struct SessionCompletedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

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

        /// <summary>Id of the <c>reportOptions</c> choice picked, for a hazard-classification
        /// task. Empty otherwise. Rides alongside <see cref="State"/> so a session log can
        /// tell a clean "completed" apart from one that completed on a wrong classification.</summary>
        public string ReportedOptionId;
    }

    [System.Serializable]
    public struct SessionStartedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        /// <summary>Total number of tasks in the loaded scenario. Lets the session logger report an
        /// accurate denominator even when the session is abandoned before SessionCompleted fires.</summary>
        public int TotalTasks;
    }
    [System.Serializable]
    public struct SessionPausedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        /// <summary>The hold that moved the session from running to paused.</summary>
        public PauseSource Source;
        /// <summary>Every source holding the pause when it began.</summary>
        public PauseSource[]? ActiveSources;
    }
    [System.Serializable]
    public struct SessionResumedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        /// <summary>The release that moved the session from paused to running.</summary>
        public PauseSource Source;
    }

    /// <summary>
    /// One pause source took or dropped its hold. Published for every change of the hold set,
    /// ahead of the SessionPaused/SessionResumed transition it may cause, so a log can rebuild
    /// overlapping pauses (popup open while the headset is off) that the transition pair merges.
    /// </summary>
    [System.Serializable]
    public struct PauseHoldChangedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }

        public PauseSource Source;
        /// <summary>True when the source took its hold, false when it released it.</summary>
        public bool Held;
        /// <summary>Sources holding the pause after this change; empty when the session runs.</summary>
        public PauseSource[]? ActiveSources;
    }
    [System.Serializable]
    public struct SessionEndedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }
    }
}
