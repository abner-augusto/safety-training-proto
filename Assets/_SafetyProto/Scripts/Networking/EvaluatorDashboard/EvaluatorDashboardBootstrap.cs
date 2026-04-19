using System;
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.Events;
using SafetyProto.Utils;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace SafetyProto.Gameplay.Networking.EvaluatorDashboard
{
    /// <summary>
    /// Bootstraps the on-device evaluator dashboard servers (HTTP + WebSocket) and event streaming.
    /// Drop this into a boot scene to expose training telemetry over LAN.
    /// </summary>
    public class EvaluatorDashboardBootstrap : MonoBehaviour
    {
        [Header("Networking")]
        public int httpPort = 8080;
        public int wsPort = 7071;

        [Header("Pose Broadcasting")]
        [SerializeField] private PoseChannelSO poseChannel;
        [SerializeField] private float poseSendRateHz = 10f;
        [SerializeField] private int poseDecimalPrecision = 3;

        [Header("Event Filtering")]
        [Tooltip("If false, reduces chatter by skipping high-volume events (ActionAttempts, PPE changes).")]
        public bool verboseEvents = true;

        [Header("Session Log Broadcast")]
        [Tooltip("Delay (seconds) before broadcasting the session log to ensure it has been written to disk.")]
        [SerializeField] private float sessionLogBroadcastDelay = 0.25f;

        private static EvaluatorDashboardBootstrap _instance;

        private MiniHttpServer _httpServer;
        private EvaluatorWebSocketServer _wsServer;
        private Coroutine _pendingLogBroadcast;
        private Coroutine _poseSendCoroutine;
        private readonly List<TaskGroup> _knownGroups = new List<TaskGroup>();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable() { } // intentionally empty — subscription moved to Start

        private void Start()
        {
            StartServers();
            if (poseChannel != null)
            {
                var poseSender = new PoseSender(poseChannel, _wsServer, poseSendRateHz, poseDecimalPrecision);
                _poseSendCoroutine = StartCoroutine(poseSender.SendLoop());
            }
            _ = LogStartupInfoAsync();
            SubscribeEvents(); // after servers are ready
        }

        private void OnDisable() { } // intentionally empty

        private void OnDestroy()
        {
            UnsubscribeEvents();
            if (_poseSendCoroutine != null)
            {
                StopCoroutine(_poseSendCoroutine);
                _poseSendCoroutine = null;
            }
            if (_pendingLogBroadcast != null)
            {
                StopCoroutine(_pendingLogBroadcast);
                _pendingLogBroadcast = null;
            }
            _httpServer?.Stop();
            _wsServer?.StopServer();
            if (_instance == this) _instance = null;
        }

        private void StartServers()
        {
            _wsServer = new EvaluatorWebSocketServer();
            _wsServer.StartServer(wsPort);

            var indexAsset = Resources.Load<TextAsset>("Dashboard/index");
            var appAsset = Resources.Load<TextAsset>("Dashboard/app");
            var styleAsset = Resources.Load<TextAsset>("Dashboard/style");

            var indexBytes = indexAsset != null ? Encoding.UTF8.GetBytes(indexAsset.text) : null;
            var appBytes = appAsset != null ? Encoding.UTF8.GetBytes(appAsset.text) : null;
            var styleBytes = styleAsset != null ? Encoding.UTF8.GetBytes(styleAsset.text) : null;

            _httpServer = new MiniHttpServer(indexBytes, appBytes, styleBytes);
            _httpServer.Start(httpPort);
        }

        private void SubscribeEvents()
        {
            EventBus.OnSessionStartedCSharp += OnSessionStarted;
            EventBus.OnSessionPausedCSharp += OnSessionPaused;
            EventBus.OnSessionResumedCSharp += OnSessionResumed;
            EventBus.OnSessionEndedCSharp += OnSessionEnded;

            EventBus.OnActionAttemptCSharp += OnActionAttempt;
            EventBus.OnPpeStateChangedCSharp += OnPpeStateChanged;

            EventBus.OnTaskStartedCSharp += OnTaskStarted;
            EventBus.OnTaskCompletedCSharp += OnTaskCompleted;
            EventBus.OnTaskTimeoutCSharp += OnTaskTimeout;

            EventBus.OnScoreChangedCSharp += OnScoreChanged;

            EventBus.OnGroupStartedCSharp += OnGroupStarted;
            EventBus.OnGroupCompletedCSharp += OnGroupCompleted;

            EventBus.OnSafetyViolationCSharp += OnSafetyViolation;
            EventBus.OnCriticalSafetyFailureCSharp += OnCriticalSafetyFailure;
            EventBus.OnSafetyErrorCSharp += OnSafetyError;

            if (EventBus.Instance != null)
            {
                EventBus.Instance.onSessionCompleted.AddListener(OnSessionCompleted);
            }
        }

        private void UnsubscribeEvents()
        {
            EventBus.OnSessionStartedCSharp -= OnSessionStarted;
            EventBus.OnSessionPausedCSharp -= OnSessionPaused;
            EventBus.OnSessionResumedCSharp -= OnSessionResumed;
            EventBus.OnSessionEndedCSharp -= OnSessionEnded;

            EventBus.OnActionAttemptCSharp -= OnActionAttempt;
            EventBus.OnPpeStateChangedCSharp -= OnPpeStateChanged;

            EventBus.OnTaskStartedCSharp -= OnTaskStarted;
            EventBus.OnTaskCompletedCSharp -= OnTaskCompleted;
            EventBus.OnTaskTimeoutCSharp -= OnTaskTimeout;

            EventBus.OnScoreChangedCSharp -= OnScoreChanged;

            EventBus.OnGroupStartedCSharp -= OnGroupStarted;
            EventBus.OnGroupCompletedCSharp -= OnGroupCompleted;

            EventBus.OnSafetyViolationCSharp -= OnSafetyViolation;
            EventBus.OnCriticalSafetyFailureCSharp -= OnCriticalSafetyFailure;
            EventBus.OnSafetyErrorCSharp -= OnSafetyError;

            if (EventBus.Instance != null)
            {
                EventBus.Instance.onSessionCompleted.RemoveListener(OnSessionCompleted);
            }
        }

        #region Event Handlers

        private void OnSessionStarted(SessionStartedEventArgs args)
        {
            Broadcast("SessionStarted", new SessionDto(args.SessionId, ResolveTimestamp(args.TimestampMs)));

            var manifest = BuildSessionManifest(args.SessionId);
            Broadcast("SessionManifest", manifest);
        }

        private void OnSessionPaused(SessionPausedEventArgs args)
        {
            Broadcast("SessionPaused", new SessionDto(args.SessionId, ResolveTimestamp(args.TimestampMs)));
        }

        private void OnSessionResumed(SessionResumedEventArgs args)
        {
            Broadcast("SessionResumed", new SessionDto(args.SessionId, ResolveTimestamp(args.TimestampMs)));
        }

        private void OnSessionEnded(SessionEndedEventArgs args)
        {
            Broadcast("SessionEnded", new SessionDto(args.SessionId, ResolveTimestamp(args.TimestampMs)));
        }

        private void OnSessionCompleted(SessionCompletedEventArgs args)
        {
            var dto = new SessionCompletedDto
            {
                sessionId = args.SessionId,
                timestampMs = ResolveTimestamp(args.TimestampMs),
                totalElapsedTime = args.totalElapsedTime,
                totalScore = args.totalScore,
                tasksCompleted = args.tasksCompleted,
                totalTasks = args.totalTasks,
                orderViolationCount = args.orderViolationCount
            };
            Broadcast("SessionCompleted", dto);
            QueueSessionLogBroadcast();
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            var group = args.Group as TaskGroup;
            var dto = new GroupDto
            {
                sessionId = args.SessionId,
                groupId = group != null ? group.groupName : string.Empty,
                groupName = group != null ? group.groupName : string.Empty,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
            if (group != null && !_knownGroups.Contains(group))
                _knownGroups.Add(group);

            Broadcast("GroupStarted", dto);
            Broadcast("SessionManifest", BuildSessionManifest(args.SessionId));
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            var group = args.Group as TaskGroup;
            var dto = new GroupDto
            {
                sessionId = args.SessionId,
                groupId = group != null ? group.groupName : string.Empty,
                groupName = group != null ? group.groupName : string.Empty,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
            Broadcast("GroupCompleted", dto);
        }

        private void OnTaskStarted(TaskEventArgs args)
        {
            Broadcast("TaskStarted", BuildTaskDto(args, "started"));
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            Broadcast("TaskCompleted", BuildTaskDto(args, "completed"));
        }

        private void OnTaskTimeout(TaskEventArgs args)
        {
            Broadcast("TaskTimeout", BuildTaskDto(args, "timeout"));
        }

        private void OnScoreChanged(ScoreChangedEventArgs args)
        {
            var dto = new ScoreDto
            {
                sessionId = args.SessionId,
                totalScore = args.TotalScore,
                delta = args.Delta,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
            Broadcast("ScoreChanged", dto);
        }

        private void OnPpeStateChanged(PPEStateChangedEventArgs args)
        {
            var dto = new PpeDto
            {
                sessionId = args.SessionId,
                ppeType = args.PpeType.ToString(),
                isWearing = args.IsWearing,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
            if (verboseEvents)
            {
                Broadcast("PpeChanged", dto);
            }
        }

        private void OnActionAttempt(ActionAttemptedEvent args)
        {
            if (!verboseEvents)
                return;
            var position = args.Position.HasValue
                ? new Vector3(args.Position.Value.X, args.Position.Value.Y, args.Position.Value.Z)
                : Vector3.zero;

            var dto = new ActionAttemptDto
            {
                sessionId = args.SessionId,
                actionId = args.ActionId,
                sourceId = args.SourceId,
                context = args.Context,
                interactorId = args.InteractorId,
                px = position.x,
                py = position.y,
                pz = position.z,
                hasPosition = args.Position.HasValue,
                time = args.TimestampMs / 1000f,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
            Broadcast("ActionAttempt", dto);
        }

        private void OnSafetyViolation(SafetyViolationEventArgs args)
        {
            var dto = new SafetyViolationDto
            {
                sessionId = args.SessionId,
                violationCode = args.ViolationCode,
                message = args.Message,
                taskId = args.TaskId,
                groupId = args.GroupId,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
            Broadcast("SafetyViolation", dto);
        }

        private void OnCriticalSafetyFailure(CriticalSafetyFailureEventArgs args)
        {
            var dto = new CriticalFailureDto
            {
                sessionId = args.SessionId,
                reason = args.Reason,
                violationCount = args.ViolationCount,
                windowSeconds = args.WindowSeconds,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
            Broadcast("CriticalSafetyFailure", dto);
        }

        private void OnSafetyError(SafetyErrorEventArgs args)
        {
            var dto = new SafetyErrorDto
            {
                sessionId = args.SessionId,
                source = args.Source,
                message = args.Message,
                details = args.Details,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
            Broadcast("SafetyError", dto);
        }

        #endregion

        private TaskDto BuildTaskDto(TaskEventArgs args, string status)
        {
            var task = args.Task as SafetyTask;
            var name = task != null ? task.taskName : string.Empty;
            var id = task != null ? task.taskName : string.Empty;
            var meta = BuildTaskMetadata(task);
            return new TaskDto
            {
                sessionId = args.SessionId,
                taskId = id,
                taskName = name,
                taskDescription = meta.description,
                hint = meta.hint,
                groupName = meta.groupName,
                order = meta.order,
                executionMode = meta.executionMode,
                expectedAction = meta.expectedAction,
                requiredPpe = meta.requiredPpe,
                successPoints = meta.successPoints,
                failurePenalty = meta.failurePenalty,
                ppePenalty = meta.ppePenalty,
                status = status,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
        }

        private TaskMetadata BuildTaskMetadata(SafetyTask task)
        {
            if (task == null || _knownGroups.Count == 0)
            {
                return TaskMetadata.Empty;
            }

            string groupName = string.Empty;
            string executionMode = string.Empty;
            int order = -1;
            int runningOrder = 1;

            foreach (var group in _knownGroups)
            {
                if (group == null || group.tasks == null)
                    continue;

                foreach (var candidate in group.tasks)
                {
                    if (candidate == null)
                    {
                        runningOrder++;
                        continue;
                    }

                    if (candidate == task)
                    {
                        groupName = group.groupName;
                        executionMode = group.executionMode.ToString();
                        order = runningOrder;
                        goto Found;
                    }

                    runningOrder++;
                }
            }

        Found:
            var required = task.requiredPPE != null
                ? task.requiredPPE.ConvertAll(p => p.ToString()).ToArray()
                : System.Array.Empty<string>();

            return new TaskMetadata
            {
                groupName = groupName,
                executionMode = executionMode,
                order = order,
                description = task.taskDescription ?? string.Empty,
                hint = task.hintText ?? string.Empty,
                expectedAction = task.ResolveExpectedActionId(),
                requiredPpe = required,
                successPoints = task.successPoints,
                failurePenalty = task.failurePenalty,
                ppePenalty = task.ppePenalty
            };
        }

        private SessionManifestDto BuildSessionManifest(string sessionId)
        {
            var dtos = new List<TaskManifestItemDto>();

            foreach (var group in _knownGroups)
            {
                if (group == null) continue;
                foreach (var task in group.tasks)
                {
                    if (task == null) continue;
                    dtos.Add(new TaskManifestItemDto
                    {
                        taskName = task.taskName,
                        groupName = group.groupName,
                        description = task.taskDescription
                    });
                }
            }

            return new SessionManifestDto
            {
                sessionId = sessionId,
                tasks = dtos.ToArray()
            };
        }

        private long ResolveTimestamp(long timestampMs)
        {
            return timestampMs != 0 ? timestampMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void ResetSession()
        {
            _knownGroups.Clear();
            var dto = new SessionResetDto
            {
                timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Broadcast("SessionReset", dto);
        }

        private void Broadcast<T>(string eventType, T payload)
        {
            _wsServer?.Broadcast(eventType, payload);
        }

        private void QueueSessionLogBroadcast()
        {
            if (!isActiveAndEnabled)
                return;

            if (_pendingLogBroadcast != null)
            {
                StopCoroutine(_pendingLogBroadcast);
            }

            _pendingLogBroadcast = StartCoroutine(BroadcastLogDelayed());
        }

        private IEnumerator BroadcastLogDelayed()
        {
            if (sessionLogBroadcastDelay > 0f)
            {
                yield return new WaitForSeconds(sessionLogBroadcastDelay);
            }
            else
            {
                yield return null;
            }

            TryBroadcastLatestSessionLog();
            _pendingLogBroadcast = null;
        }

        private void TryBroadcastLatestSessionLog()
        {
            try
            {
                var dir = Application.persistentDataPath;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return;

                var files = Directory.GetFiles(dir, "session_log_*.json");
                if (files == null || files.Length == 0)
                    return;

                string latestFile = null;
                DateTime latestTime = DateTime.MinValue;
                foreach (var f in files)
                {
                    var t = File.GetLastWriteTimeUtc(f);
                    if (t > latestTime)
                    {
                        latestTime = t;
                        latestFile = f;
                    }
                }

                if (string.IsNullOrEmpty(latestFile))
                    return;

                var content = File.ReadAllText(latestFile);
                var payload = new SessionLogFileDto
                {
                    fileName = Path.GetFileName(latestFile),
                    path = latestFile,
                    content = content
                };
                Broadcast("SessionLogFile", payload);
            }
            catch (Exception ex)
            {
                SafetyLog.Warning($"Failed to broadcast session log file: {ex.Message}", this);
            }
        }

        private async Awaitable LogStartupInfoAsync()
        {
            string ip = await System.Threading.Tasks.Task.Run(TryGetLocalIPv4);
            SafetyLog.Info($"Evaluator Dashboard servers started. HTTP=http://{ip}:{httpPort} WS=ws://{ip}:{wsPort}/eval", this);
        }

        private string TryGetLocalIPv4()
        {
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var props = networkInterface.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
            return "0.0.0.0";
        }

        #region DTOs

        [Serializable]
        private struct SessionDto
        {
            public string sessionId;
            public long timestampMs;

            public SessionDto(string sessionId, long timestamp)
            {
                this.sessionId = sessionId;
                timestampMs = timestamp;
            }
        }

        [Serializable]
        private struct SessionCompletedDto
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
        private struct GroupDto
        {
            public string sessionId;
            public string groupId;
            public string groupName;
            public long timestampMs;
        }

        [Serializable]
        private struct TaskDto
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
            public int failurePenalty;
            public int ppePenalty;
            public string status;
            public long timestampMs;
        }

        private struct TaskMetadata
        {
            public string groupName;
            public string executionMode;
            public int order;
            public string description;
            public string hint;
            public string expectedAction;
            public string[] requiredPpe;
            public int successPoints;
            public int failurePenalty;
            public int ppePenalty;

            public static TaskMetadata Empty => new TaskMetadata
            {
                groupName = string.Empty,
                executionMode = string.Empty,
                order = -1,
                description = string.Empty,
                hint = string.Empty,
                expectedAction = string.Empty,
                requiredPpe = System.Array.Empty<string>(),
                successPoints = 0,
                failurePenalty = 0,
                ppePenalty = 0
            };
        }

        [Serializable]
        private struct ScoreDto
        {
            public string sessionId;
            public int totalScore;
            public int delta;
            public long timestampMs;
        }

        [Serializable]
        private struct PpeDto
        {
            public string sessionId;
            public string ppeType;
            public bool isWearing;
            public long timestampMs;
        }

        [Serializable]
        private struct ActionAttemptDto
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
        private struct SafetyViolationDto
        {
            public string sessionId;
            public string violationCode;
            public string message;
            public string taskId;
            public string groupId;
            public long timestampMs;
        }

        [Serializable]
        private struct CriticalFailureDto
        {
            public string sessionId;
            public string reason;
            public int violationCount;
            public float windowSeconds;
            public long timestampMs;
        }

        [Serializable]
        private struct SafetyErrorDto
        {
            public string sessionId;
            public string source;
            public string message;
            public string details;
            public long timestampMs;
        }

        [Serializable]
        private struct SessionLogFileDto
        {
            public string fileName;
            public string path;
            public string content;
        }

        [Serializable]
        private struct SessionManifestDto
        {
            public string sessionId;
            public TaskManifestItemDto[] tasks;
        }

        [Serializable]
        private struct TaskManifestItemDto
        {
            public string taskName;
            public string groupName;
            public string description;
        }

        [Serializable]
        private struct SessionResetDto
        {
            public long timestampMs;
        }

        #endregion
    }
}
