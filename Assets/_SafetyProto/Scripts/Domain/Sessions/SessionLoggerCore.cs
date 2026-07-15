#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;

namespace SafetyProto.Domain.Sessions
{
    public sealed class SessionLoggerCore : IDisposable
    {
        /// <summary>
        /// Serializer for the session log. Both hosts (Unity wrapper and CLI harness) pass
        /// <see cref="SerializeIndentedOmittingDefaults"/> as the injected
        /// <c>Func&lt;SessionLog, string&gt;</c>. It uses Newtonsoft.Json (a shared dependency of the
        /// Domain assembly) with <c>DefaultValueHandling.Ignore</c> so each entry emits only the
        /// fields that actually carry a value — an all-default <see cref="LogData"/> is dropped
        /// entirely instead of the old wall of empty fields. Newtonsoft serializes the public fields
        /// of these DTOs by default, so no attributes are required.
        /// </summary>
        public static string SerializeIndentedOmittingDefaults(SessionLog log) =>
            Newtonsoft.Json.JsonConvert.SerializeObject(
                log,
                Newtonsoft.Json.Formatting.Indented,
                new Newtonsoft.Json.JsonSerializerSettings
                {
                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                    DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore
                });

        /// <summary>
        /// Typed, structured counterpart to <see cref="LogEntry.details"/>. Lets the
        /// web dashboard format and localize the report detail line instead of relying
        /// on the backend's English string. Only the events that carry English
        /// scaffolding populate this; the rest leave it at <c>default</c> and the
        /// frontend falls back to <see cref="LogEntry.details"/>. Field names are reused
        /// across events (e.g. <c>message</c>, <c>totalScore</c>) to keep the struct small.
        /// It is a value type so both <c>JsonUtility</c> and <c>System.Text.Json</c>
        /// always serialize it inline.
        /// </summary>
        [Serializable]
        public struct LogData
        {
            // ActionAttempt (stable id; the friendly name goes in LogEntry.details)
            public string actionId;
            // PpeStateChanged
            public string ppeType;
            public bool wearing;
            // ScoreChanged (totalScore reused by SessionCompleted)
            public int delta;
            public int totalScore;
            // SafetyViolation (message reused by SafetyError)
            public string violationCode;
            public string message;
            public string taskId;
            public string groupId;
            // SafetyError
            public string source;
            public string errorDetails;
            // CriticalSafetyFailure
            public string reason;
            public int violationCount;
            public float windowSeconds;
            // SessionCompleted
            public float totalElapsedTime;
            public int tasksCompleted;
            public int totalTasks;
        }

        [Serializable]
        public sealed class LogEntry
        {
            public string timestamp = string.Empty;
            public string eventName = string.Empty;
            public string details = string.Empty;
            public LogData data;
            public string sessionId = string.Empty;
            public string playerId = string.Empty;
            public string scenarioId = string.Empty;
            public long timestampMs;
        }

        [Serializable]
        public sealed class SessionSummary
        {
            /// <summary>
            /// <c>true</c> only when the session reached <c>SessionCompleted</c>. When the
            /// participant resets or abandons mid-session the summary is synthesized from the
            /// events logged so far and this stays <c>false</c> (and <see cref="totalTasks"/>
            /// is 0/unknown), so a reset session no longer masquerades as an all-zero completion.
            /// </summary>
            public bool completed;
            /// <summary>"guiado" | "avaliacao" — captured at SessionStarted so collected
            /// data can be split by mode. Empty on logs written before this field existed.</summary>
            public string mode = string.Empty;
            public float totalElapsedTime;
            public int totalScore;
            public int tasksCompleted;
            public int totalTasks;
        }

        [Serializable]
        public sealed class SessionLog
        {
            public List<LogEntry> entries = new List<LogEntry>();
            public SessionSummary? summary;
        }

        private readonly IEventBus _eventBus;
        private readonly string _outputDirectory;
        private readonly SessionLog _log = new SessionLog();
        private readonly Func<SessionLog, string> _serialize;
        private readonly IHarnessLogger? _logger;

        /// <summary>
        /// Optional actionId → friendly-name resolver (host injects it, backed by the action
        /// catalog). When null the raw action id is logged as the detail, preserving old behavior.
        /// </summary>
        private readonly Func<string, string>? _actionNameResolver;

        private readonly Action<SessionStartedEventArgs>          _onSessionStarted;
        private readonly Action<SessionPausedEventArgs>           _onSessionPaused;
        private readonly Action<SessionResumedEventArgs>          _onSessionResumed;
        private readonly Action<SessionCompletedEventArgs>        _onSessionCompleted;
        private readonly Action<ActionAttemptedEvent>             _onActionAttempt;
        private readonly Action<PPEStateChangedEventArgs>         _onPpeStateChanged;
        private readonly Action<TaskEventArgs>                    _onTaskLifecycle;
        private readonly Action<ScoreChangedEventArgs>            _onScoreChanged;
        private readonly Action<TaskGroupEventArgs>               _onGroupLifecycle;
        private readonly Action<SafetyViolationEventArgs>         _onSafetyViolation;
        private readonly Action<SafetyErrorEventArgs>             _onSafetyError;
        private readonly Action<CriticalSafetyFailureEventArgs>   _onCriticalSafetyFailure;

        private bool _subscribed;
        private bool _disposed;

        // Running tallies, kept so a summary can be synthesized when the session is reset or
        // abandoned before SessionCompleted fires (see BuildFallbackSummary / WriteLogAsync).
        private long _sessionStartMs;
        private long _lastEventMs;
        private int _lastTotalScore;
        private int _tasksCompletedCount;
        private int _totalTasks;
        private string _sessionId = string.Empty;
        private string _mode = string.Empty;

        public SessionLoggerCore(IEventBus eventBus, string outputDirectory, Func<SessionLog, string> serialize, IHarnessLogger? logger = null, Func<string, string>? actionNameResolver = null)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            _serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            _logger = logger;
            _actionNameResolver = actionNameResolver;

            _onSessionStarted        = args =>
            {
                ResetTallies(args.TimestampMs);
                _totalTasks = args.TotalTasks;
                _sessionId = args.SessionId ?? string.Empty;
                _mode = SessionModeState.CurrentName;
                LogEvent("SessionStarted", string.Empty, _sessionId, args.PlayerId, args.ScenarioId, args.TimestampMs);
            };
            _onSessionPaused         = args => LogEvent("SessionPaused",     string.Empty, args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs);
            _onSessionResumed        = args => LogEvent("SessionResumed",    string.Empty, args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs);
            _onSessionCompleted      = OnSessionCompleted;
            _onActionAttempt         = args =>
            {
                var actionId = args.ActionId ?? string.Empty;
                var friendly = _actionNameResolver?.Invoke(actionId);
                var details = string.IsNullOrWhiteSpace(friendly) ? actionId : friendly;
                LogEvent("ActionAttempt", details, args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                    new LogData { actionId = actionId });
            };
            _onPpeStateChanged       = args => LogEvent("PpeStateChanged",   $"PPE={args.PpeType}, Wearing={args.IsWearing}", args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                new LogData { ppeType = args.PpeType.ToString(), wearing = args.IsWearing });
            _onTaskLifecycle = args =>
            {
                string eventName = args.Phase switch
                {
                    TaskPhase.Started => "TaskStarted",
                    TaskPhase.Completed => "TaskCompleted",
                    TaskPhase.Timeout => "TaskTimeout",
                    _ => "TaskUnknown"
                };
                if (args.Phase == TaskPhase.Completed) _tasksCompletedCount++;
                LogEvent(eventName, args.Task?.taskName ?? string.Empty,
                    args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                    new LogData { taskId = args.Task?.id ?? string.Empty });
            };
            _onScoreChanged = args =>
            {
                _lastTotalScore = args.TotalScore;
                LogEvent("ScoreChanged", $"Delta={args.Delta}, Total={args.TotalScore}",
                    args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                    new LogData { delta = args.Delta, totalScore = args.TotalScore, taskId = args.TaskId, reason = args.Reason });
            };
            _onGroupLifecycle = args =>
            {
                string eventName = args.Phase switch
                {
                    TaskGroupPhase.Started => "GroupStarted",
                    TaskGroupPhase.Completed => "GroupCompleted",
                    _ => "GroupUnknown"
                };
                LogEvent(eventName, args.Group?.groupName ?? string.Empty,
                    args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                    new LogData { groupId = args.Group?.id ?? string.Empty });
            };
            _onSafetyViolation       = args => LogEvent("SafetyViolation",   $"{args.ViolationCode} | {args.Message} (Task={args.TaskName}, Group={args.GroupName})", args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                new LogData { violationCode = args.ViolationCode, message = args.Message, taskId = args.TaskId, groupId = args.GroupId });
            _onSafetyError           = args => LogEvent("SafetyError",       $"{args.Source}: {args.Message} ({args.Details})", args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                new LogData { source = args.Source, message = args.Message, errorDetails = args.Details });
            _onCriticalSafetyFailure = args => LogEvent("CriticalSafetyFailure", $"{args.Reason} [{args.ViolationCount} in {args.WindowSeconds}s]", args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                new LogData { reason = args.Reason, violationCount = args.ViolationCount, windowSeconds = args.WindowSeconds });
        }

        public void Subscribe()
        {
            if (_subscribed) return;
            _eventBus.Subscribe(_onSessionStarted);
            _eventBus.Subscribe(_onSessionPaused);
            _eventBus.Subscribe(_onSessionResumed);
            _eventBus.Subscribe(_onSessionCompleted);
            _eventBus.Subscribe(_onActionAttempt);
            _eventBus.Subscribe(_onPpeStateChanged);
            _eventBus.Subscribe(_onTaskLifecycle);
            _eventBus.Subscribe(_onScoreChanged);
            _eventBus.Subscribe(_onGroupLifecycle);
            _eventBus.Subscribe(_onSafetyViolation);
            _eventBus.Subscribe(_onSafetyError);
            _eventBus.Subscribe(_onCriticalSafetyFailure);
            _subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!_subscribed) return;
            _eventBus.Unsubscribe(_onSessionStarted);
            _eventBus.Unsubscribe(_onSessionPaused);
            _eventBus.Unsubscribe(_onSessionResumed);
            _eventBus.Unsubscribe(_onSessionCompleted);
            _eventBus.Unsubscribe(_onActionAttempt);
            _eventBus.Unsubscribe(_onPpeStateChanged);
            _eventBus.Unsubscribe(_onTaskLifecycle);
            _eventBus.Unsubscribe(_onScoreChanged);
            _eventBus.Unsubscribe(_onGroupLifecycle);
            _eventBus.Unsubscribe(_onSafetyViolation);
            _eventBus.Unsubscribe(_onSafetyError);
            _eventBus.Unsubscribe(_onCriticalSafetyFailure);
            _subscribed = false;
        }

        private void OnSessionCompleted(SessionCompletedEventArgs args)
        {
            var details = string.Format(CultureInfo.InvariantCulture,
                "Time={0}, Score={1}, Completed={2}/{3}",
                args.totalElapsedTime, args.totalScore, args.tasksCompleted, args.totalTasks);
            LogEvent("SessionCompleted", details,
                args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                new LogData
                {
                    totalElapsedTime = args.totalElapsedTime,
                    totalScore = args.totalScore,
                    tasksCompleted = args.tasksCompleted,
                    totalTasks = args.totalTasks
                });

            _log.summary = new SessionSummary
            {
                completed = true,
                mode = _mode,
                totalElapsedTime = args.totalElapsedTime,
                totalScore = args.totalScore,
                tasksCompleted = args.tasksCompleted,
                totalTasks = args.totalTasks
            };

            _ = WriteLogAsync();
        }

        private void LogEvent(string eventName, string details, string sessionId, string playerId, string scenarioId, long timestampMs, LogData data = default)
        {
            long actualTimestamp = timestampMs == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : timestampMs;
            var timestampIso = DateTimeOffset.FromUnixTimeMilliseconds(actualTimestamp).ToString("o");
            _lastEventMs = actualTimestamp;

            _log.entries.Add(new LogEntry
            {
                timestamp = timestampIso,
                eventName = eventName,
                details = details ?? string.Empty,
                data = data,
                sessionId = sessionId ?? string.Empty,
                playerId = playerId ?? string.Empty,
                scenarioId = scenarioId ?? string.Empty,
                timestampMs = actualTimestamp
            });
        }

        private void ResetTallies(long timestampMs)
        {
            _sessionStartMs = timestampMs == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : timestampMs;
            _lastEventMs = _sessionStartMs;
            _lastTotalScore = 0;
            _tasksCompletedCount = 0;
            _totalTasks = 0;
            _sessionId = string.Empty;
            _mode = string.Empty;
        }

        private SessionSummary BuildFallbackSummary() => new SessionSummary
        {
            completed = false,
            mode = _mode,
            totalScore = _lastTotalScore,
            tasksCompleted = _tasksCompletedCount,
            totalTasks = _totalTasks, // from SessionStarted; 0 only if that event never carried it
            totalElapsedTime = _sessionStartMs > 0 ? (_lastEventMs - _sessionStartMs) / 1000f : 0f
        };

        public async Task<string?> WriteLogAsync()
        {
            try
            {
                if (!Directory.Exists(_outputDirectory))
                {
                    Directory.CreateDirectory(_outputDirectory);
                }

                // Name the file after the SESSION START (not the wall-clock time of this write), so
                // the two writes a normal session makes — one on SessionCompleted, one on the restart
                // ResetSession — resolve to the same path and the later write overwrites the earlier
                // one. Previously each write minted a new timestamped file, littering the folder with
                // near-duplicate pairs. A short session-id suffix disambiguates sessions that happen to
                // start within the same second.
                var startMs = _sessionStartMs > 0 ? _sessionStartMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(startMs).UtcDateTime.ToString("yyyyMMdd_HHmmss");
                var idSuffix = string.IsNullOrEmpty(_sessionId)
                    ? string.Empty
                    : "_" + _sessionId.Substring(0, Math.Min(8, _sessionId.Length));
                var fileName = $"session_log_{timestamp}{idSuffix}.json";
                var path = Path.Combine(_outputDirectory, fileName);

                // Synthesize an accurate summary from the events logged so far when the session
                // ended without a SessionCompleted event (reset / abandoned). Guarantees the file
                // never carries the old all-zero placeholder summary.
                _log.summary ??= BuildFallbackSummary();

                var json = _serialize(_log);
                await File.WriteAllTextAsync(path, json);

                _logger?.Info($"[SessionLogger] Log written to: {path}");
                return path;
            }
            catch (Exception ex)
            {
                _logger?.Error($"[SessionLogger] Failed to write log: {ex.Message}");
                return null;
            }
        }

        public void ResetSession()
        {
            LogEvent(
                "SessionReset",
                "User manually triggered session reset",
                EventContext.CurrentSessionId ?? string.Empty,
                EventContext.CurrentPlayerId  ?? string.Empty,
                EventContext.CurrentScenarioId ?? string.Empty,
                EventContext.NowUnixMs());

            _ = WriteLogAsync();
            _log.entries.Clear();
            _log.summary = null;
            ResetTallies(0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Unsubscribe();
            _disposed = true;
        }
    }
}
