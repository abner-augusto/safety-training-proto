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
        /// JSON DTOs use public fields (not properties) and carry the
        /// <c>[Serializable]</c> attribute for compatibility with
        /// <c>UnityEngine.JsonUtility</c>, which the Unity wrapper injects as
        /// its serializer via the <c>Func&lt;SessionLog, string&gt;</c> constructor
        /// parameter. Unity ships a restricted <c>System.Text.Json</c> assembly
        /// where <c>JsonSerializerOptions</c> is inaccessible, forcing this
        /// injection-based design.
        ///
        /// When the CLI harness (net10.0 target, Part 8) serializes these types
        /// via <c>System.Text.Json.JsonSerializer</c>, it MUST configure:
        ///
        /// <code>
        /// new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }
        /// </code>
        ///
        /// because <c>System.Text.Json</c> ignores fields by default. Without this
        /// flag the harness would silently emit empty objects.
        /// </summary>
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

        public SessionLoggerCore(IEventBus eventBus, string outputDirectory, Func<SessionLog, string> serialize, IHarnessLogger? logger = null)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            _serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            _logger = logger;

            _onSessionStarted        = args => LogEvent("SessionStarted",    string.Empty, args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs);
            _onSessionPaused         = args => LogEvent("SessionPaused",     string.Empty, args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs);
            _onSessionResumed        = args => LogEvent("SessionResumed",    string.Empty, args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs);
            _onSessionCompleted      = OnSessionCompleted;
            _onActionAttempt         = args => LogEvent("ActionAttempt",     args.ActionId ?? string.Empty, args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs);
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
                LogEvent(eventName, args.Task?.taskName ?? string.Empty,
                    args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs);
            };
            _onScoreChanged = args => LogEvent("ScoreChanged", $"Delta={args.Delta}, Total={args.TotalScore}",
                args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
                new LogData { delta = args.Delta, totalScore = args.TotalScore });
            _onGroupLifecycle = args =>
            {
                string eventName = args.Phase switch
                {
                    TaskGroupPhase.Started => "GroupStarted",
                    TaskGroupPhase.Completed => "GroupCompleted",
                    _ => "GroupUnknown"
                };
                LogEvent(eventName, args.Group?.groupName ?? string.Empty,
                    args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs);
            };
            _onSafetyViolation       = args => LogEvent("SafetyViolation",   $"{args.ViolationCode} | {args.Message} (Task={args.TaskId}, Group={args.GroupId})", args.SessionId, args.PlayerId, args.ScenarioId, args.TimestampMs,
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

        public async Task<string?> WriteLogAsync()
        {
            try
            {
                if (!Directory.Exists(_outputDirectory))
                {
                    Directory.CreateDirectory(_outputDirectory);
                }

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var fileName = $"session_log_{timestamp}.json";
                var path = Path.Combine(_outputDirectory, fileName);

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
        }

        public void Dispose()
        {
            if (_disposed) return;
            Unsubscribe();
            _disposed = true;
        }
    }
}
