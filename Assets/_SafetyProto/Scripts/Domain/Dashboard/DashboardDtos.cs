#nullable enable annotations

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
        /// <summary>Who set the participant id: "dashboard", "keypad", "skipped", "simulator".</summary>
        public string? participantSource;
        /// <summary>The interrupted session this one restarts; empty for a first attempt.</summary>
        public string? previousSessionId;
        public int attempt;
        public long timestampMs;

        public SessionDto(string sessionId, long timestamp)
        {
            this.sessionId = sessionId;
            participantId = null;
            mode = null;
            participantSource = null;
            previousSessionId = null;
            attempt = 0;
            timestampMs = timestamp;
        }
    }

    /// <summary>
    /// What the headset is doing and how it is holding up, so the evaluator knows when it is
    /// ready for a start command and sees the battery before it dies mid-session. Sent on
    /// RequestSync, after every command, on session start/completion and periodically.
    /// </summary>
    [Serializable]
    public struct StationStatusDto
    {
        /// <summary>The headset shows the "waiting for the researcher" screen.</summary>
        public bool awaitingStart;
        /// <summary>No dashboard connected in time, so the headset let the participant type the code.</summary>
        public bool participantKeypadOpen;
        public bool sessionActive;
        public bool sessionCompleted;
        /// <summary>PauseSource names holding the session right now; empty while it runs.</summary>
        public string[] pauseSources;
        public string participantId;
        /// <summary>"guiado" | "avaliacao"; empty before the first session.</summary>
        public string mode;
        public int attempt;
        public string deviceModel;
        public string appVersion;
        /// <summary>0..1, or -1 when the platform does not report it.</summary>
        public float batteryLevel;
        /// <summary>UnityEngine.BatteryStatus name: Charging, Discharging, NotCharging, Full, Unknown.</summary>
        public string batteryStatus;
        public long timestampMs;
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
    public struct PlayspaceNoticeDto
    {
        public string sessionId;
        /// <summary>PlayspaceNoticeKind name: LeftWalkableArea, ReturnedToWalkableArea, RoomCenterDiscarded.</summary>
        public string kind;
        public string area;
        public float durationSeconds;
        public long timestampMs;
    }

    /// <summary>A guided hint; level 3 raises the stuck alert on the evaluator's tile.</summary>
    [Serializable]
    public struct HintDto
    {
        public string sessionId;
        /// <summary>1 = the next object pulses, 2 = the step is said aloud, 3 = the evaluator is alerted.</summary>
        public int level;
        public string taskId;
        public string taskName;
        public float secondsWithoutProgress;
        public long timestampMs;
    }

    /// <summary>SessionPaused, SessionResumed and PauseHoldChanged. Tokens are PauseSource names.</summary>
    [Serializable]
    public struct PauseDto
    {
        public string sessionId;
        /// <summary>The hold that started (SessionPaused), ended (SessionResumed) or changed (PauseHoldChanged).</summary>
        public string pauseSource;
        /// <summary>Sources still holding the pause after the event; empty once the session runs.</summary>
        public string[] pauseSources;
        public bool held;
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
        /// <summary>Portuguese, operator-facing: why the command was refused, or an optional result
        /// summary when it was accepted ("3 EPIs devolvidos à mesa").</summary>
        public string reason;
    }

    /// <summary>One object the evaluator can make pulse with highlight_target.</summary>
    [Serializable]
    public struct HighlightTargetDto
    {
        public string id;
        /// <summary>Portuguese label shown in the dashboard's picker.</summary>
        public string label;
        /// <summary>Grouping token: ppe, distractor, slot, piece, socket, anchor, lanyard, action, tool.</summary>
        public string kind;
    }

    /// <summary>Every attention target of the loaded scene, sent on every RequestSync.</summary>
    [Serializable]
    public struct HighlightTargetsDto
    {
        public HighlightTargetDto[] targets;
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
