using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using UnityEngine;

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
        public PoseBroadcaster poseBroadcaster;

        [Header("Event Filtering")]
        [Tooltip("If false, reduces chatter by skipping high-volume events (ActionAttempts, PPE changes).")]
        public bool verboseEvents = true;

        private MiniHttpServer _httpServer;
        private EvaluatorWebSocketServer _wsServer;

        private void OnEnable()
        {
            if (!this.IsEventBusReady())
                return;

            SubscribeEvents();
        }

        private void Start()
        {
            StartServers();
            if (poseBroadcaster != null)
            {
                poseBroadcaster.Initialize(_wsServer);
            }
            LogStartupInfo();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void OnDestroy()
        {
            _httpServer?.Stop();
            _wsServer?.StopServer();
        }

        private void StartServers()
        {
            _wsServer = new EvaluatorWebSocketServer();
            _wsServer.StartServer(wsPort);

            var indexAsset = Resources.Load<TextAsset>("Dashboard/index");
            var appAsset = Resources.Load<TextAsset>("Dashboard/app");
            var styleAsset = Resources.Load<TextAsset>("Dashboard/style");

            var indexBytes = indexAsset != null ? Encoding.UTF8.GetBytes(indexAsset.text) : Array.Empty<byte>();
            var appBytes = appAsset != null ? Encoding.UTF8.GetBytes(appAsset.text) : Array.Empty<byte>();
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
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            var dto = new GroupDto
            {
                sessionId = args.SessionId,
                groupId = args.Group != null ? args.Group.groupName : string.Empty,
                groupName = args.Group != null ? args.Group.groupName : string.Empty,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
            Broadcast("GroupStarted", dto);
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            var dto = new GroupDto
            {
                sessionId = args.SessionId,
                groupId = args.Group != null ? args.Group.groupName : string.Empty,
                groupName = args.Group != null ? args.Group.groupName : string.Empty,
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

        private void OnActionAttempt(ActionAttemptEventArgs args)
        {
            if (!verboseEvents)
                return;

            var dto = new ActionAttemptDto
            {
                sessionId = args.SessionId,
                actionType = args.ActionType.ToString(),
                interactorId = args.InteractorId,
                px = args.WorldPosition.x,
                py = args.WorldPosition.y,
                pz = args.WorldPosition.z,
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
            var name = args.Task != null ? args.Task.taskName : string.Empty;
            var id = args.Task != null ? args.Task.taskName : string.Empty;
            return new TaskDto
            {
                sessionId = args.SessionId,
                taskId = id,
                taskName = name,
                status = status,
                timestampMs = ResolveTimestamp(args.TimestampMs)
            };
        }

        private long ResolveTimestamp(long timestampMs)
        {
            return timestampMs != 0 ? timestampMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void Broadcast<T>(string eventType, T payload)
        {
            _wsServer?.Broadcast(eventType, payload);
        }

        private void LogStartupInfo()
        {
            var ip = TryGetLocalIPv4();
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
            public string status;
            public long timestampMs;
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
            public string actionType;
            public int interactorId;
            public float px;
            public float py;
            public float pz;
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

        #endregion
    }
}
