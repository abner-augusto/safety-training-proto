using System;

namespace SafetyProto.Domain.Dashboard
{
    // Wire DTOs for the evaluator dashboard WebSocket; its JS reads these field names, so do not rename them.
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

    /// <summary>Static scene geometry for the dashboard viewport, sent on every RequestSync.
    /// Positions and rotations are Unity world space; the dashboard mirrors Z.</summary>
    [Serializable]
    public struct SceneLayoutDto
    {
        public SceneFloorDto[] floors;
        public SceneMarkerDto[] markers;
    }

    /// <summary>Walkable floor rectangle: centre of its top face, X/Z extent, rotation.</summary>
    [Serializable]
    public struct SceneFloorDto
    {
        public string id;
        public float px, py, pz;
        public float sx, sz;
        public float qx, qy, qz, qw;
    }

    /// <summary>Oriented box marking where a task happens. State comes from the task, not from here.</summary>
    [Serializable]
    public struct SceneMarkerDto
    {
        public string id;
        public string kind;
        public string taskId;
        public float px, py, pz;
        public float sx, sy, sz;
        public float qx, qy, qz, qw;
    }
}
