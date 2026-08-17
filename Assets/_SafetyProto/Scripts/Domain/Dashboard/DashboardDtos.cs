using System;

namespace SafetyProto.Domain.Dashboard
{
    // Wire DTOs broadcast to the evaluator dashboard over WebSocket. Field names are the
    // protocol - the dashboard JS reads them off the JSON, so never rename a field.

    [Serializable]
    public struct SessionDto
    {
        public string sessionId;
        public string? participantId;
        public string? mode;
        public long timestampMs;

        public SessionDto(string sessionId, long timestamp)
        {
            this.sessionId = sessionId;
            participantId = null;
            mode = null;
            timestampMs = timestamp;
        }
    }

    [Serializable]
    public struct SessionCompletedDto
    {
        public string sessionId;
        public long timestampMs;
        public float totalElapsedTime;
        public int totalScore;
        public int tasksCompleted;
        public int totalTasks;
        public int orderViolationCount;
    }

    [Serializable]
    public struct GroupDto
    {
        public string sessionId;
        public string groupId;
        public string groupName;
        public long timestampMs;
    }

    [Serializable]
    public struct TaskDto
    {
        public string sessionId;
        public string taskId;
        public string taskName;
        public string taskDescription;
        public string hint;
        public string groupName;
        public int order;
        public string executionMode;
        public string expectedAction;
        public string[] requiredPpe;
        public int successPoints;
        // Points lost to an unsafe (missing-PPE) completion, derived from the scenario's
        // ScoringConfig - no longer an authored per-task field. There is no counterpart for
        // a task left undone: that outcome forfeits successPoints and subtracts nothing.
        public int ppePenalty;
        public string status;
        public long timestampMs;
    }

    [Serializable]
    public struct ScoreDto
    {
        public string sessionId;
        public int totalScore;
        public int delta;
        public long timestampMs;
    }

    [Serializable]
    public struct PpeDto
    {
        public string sessionId;
        public string ppeType;
        public bool isWearing;
        public long timestampMs;
    }

    [Serializable]
    public struct ActionAttemptDto
    {
        public string sessionId;
        public string actionId;
        public string sourceId;
        public string context;
        public int interactorId;
        public float px;
        public float py;
        public float pz;
        public bool hasPosition;
        public float time;
        public long timestampMs;
    }

    [Serializable]
    public struct SafetyViolationDto
    {
        public string sessionId;
        public string violationCode;
        public string message;
        public string taskId;
        public string groupId;
        public long timestampMs;
    }

    [Serializable]
    public struct CriticalFailureDto
    {
        public string sessionId;
        public string reason;
        public int violationCount;
        public float windowSeconds;
        public long timestampMs;
    }

    [Serializable]
    public struct SafetyErrorDto
    {
        public string sessionId;
        public string source;
        public string message;
        public string details;
        public long timestampMs;
    }

    [Serializable]
    public struct SessionLogFileDto
    {
        public string sessionId;
        public string participantId;
        public string fileName;
        public string path;
        public string content;
    }

    [Serializable]
    public struct SessionManifestDto
    {
        public string sessionId;
        public TaskManifestItemDto[] tasks;
    }

    [Serializable]
    public struct TaskManifestItemDto
    {
        public string taskId;
        public string taskName;
        public string groupName;
        public string description;
        public int order;
        public string status;
    }

    [Serializable]
    public struct SessionResetDto
    {
        public long timestampMs;
    }

    /// <summary>Ack for an inbound "Command" envelope (see EvaluatorDashboardBootstrap). Without
    /// this the evaluator clicks blind — the headset is on the participant's head and the
    /// dashboard is their only window.</summary>
    [Serializable]
    public struct CommandAckDto
    {
        public string requestId;
        public string command;
        public bool accepted;
        /// <summary>Portuguese, operator-facing. Empty when accepted.</summary>
        public string reason;
    }
}
