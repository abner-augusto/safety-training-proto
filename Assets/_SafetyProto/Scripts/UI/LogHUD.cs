using System.Collections.Generic;
using System.Text;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    public class LogHUD : MonoBehaviour
    {
        [Tooltip("Maximum number of messages to keep in the log.")]
        public int maxLines = 20;
        [Tooltip("Assign the TextMeshProUGUI that renders the HUD. Falls back to first child if empty.")]
        [SerializeField] private TextMeshProUGUI logText;

        private readonly Queue<string> _entries = new();
        private readonly StringBuilder _allLogs = new();
        private readonly StringBuilder _displayBuilder = new();

        private void OnEnable()
        {
            if (!this.IsEventBusReady())
                return;

            if (!TryInitializeLogText())
                return;

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

        private void OnDisable()
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

        private bool TryInitializeLogText()
        {
            if (logText == null)
            {
                logText = GetComponentInChildren<TextMeshProUGUI>();
            }

            if (logText == null)
            {
                SafetyLog.Error("LogHUD: logText is null! Please assign it in the inspector.", this);
                enabled = false;
                return false;
            }

            logText.text = "LogHUD Active";
            return true;
        }

        private void OnSessionStarted(SessionStartedEventArgs args) => AppendLog("[Session] Started");

        private void OnSessionPaused(SessionPausedEventArgs args) => AppendLog("[Session] Paused");

        private void OnSessionResumed(SessionResumedEventArgs args) => AppendLog("[Session] Resumed");

        private void OnSessionEnded(SessionEndedEventArgs args) => AppendLog("[Session] Ended");

        private void OnSessionCompleted(SessionCompletedEventArgs args)
        {
            int minutes = Mathf.FloorToInt(args.totalElapsedTime / 60f);
            int seconds = Mathf.FloorToInt(args.totalElapsedTime % 60f);
            string formattedTime = $"{minutes:00}:{seconds:00}";
            AppendLog($"[Session] Completed | Time={formattedTime} | Score={args.totalScore} | Tasks={args.tasksCompleted}/{args.totalTasks} | OrderViol={args.orderViolationCount}");
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            string groupName = args.Group != null ? args.Group.groupName : "<Unnamed Group>";
            AppendLog($"[Group] Started '{groupName}'");
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            string groupName = args.Group != null ? args.Group.groupName : "<Unnamed Group>";
            AppendLog($"[Group] Completed '{groupName}'");
        }

        private void OnTaskStarted(TaskEventArgs args)
        {
            string taskName = args.Task != null ? args.Task.taskName : "<Unnamed Task>";
            AppendLog($"[Task] Started '{taskName}'");
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            string taskName = args.Task != null ? args.Task.taskName : "<Unnamed Task>";
            AppendLog($"[Task] Completed '{taskName}'");
        }

        private void OnTaskTimeout(TaskEventArgs args)
        {
            string taskName = args.Task != null ? args.Task.taskName : "<Unnamed Task>";
            AppendLog($"[Task] TIMEOUT '{taskName}'");
        }

        private void OnScoreChanged(ScoreChangedEventArgs args)
        {
            string sign = args.Delta >= 0 ? "+" : string.Empty;
            AppendLog($"[Score] {sign}{args.Delta} (Total={args.TotalScore})");
        }

        private void OnPpeStateChanged(PPEStateChangedEventArgs args)
        {
            AppendLog($"[PPE] {args.PpeType}: {(args.IsWearing ? "WORN" : "REMOVED")}");
        }

        private void OnActionAttempt(ActionAttemptedEvent args)
        {
            var positionText = args.Position.HasValue
                ? $"({args.Position.Value.X:F2}, {args.Position.Value.Y:F2}, {args.Position.Value.Z:F2})"
                : "<no position>";
            AppendLog($"[Action] {args.ActionId} @ {positionText}");
        }

        private void OnSafetyViolation(SafetyViolationEventArgs args)
        {
            string code = string.IsNullOrEmpty(args.ViolationCode) ? "UNKNOWN" : args.ViolationCode;
            string message = string.IsNullOrEmpty(args.Message) ? "No details" : args.Message;
            string taskId = string.IsNullOrEmpty(args.TaskId) ? "-" : args.TaskId;
            string groupId = string.IsNullOrEmpty(args.GroupId) ? "-" : args.GroupId;
            AppendLog($"[Safety] VIOLATION {code} | {message} (Task={taskId}, Group={groupId})");
        }

        private void OnCriticalSafetyFailure(CriticalSafetyFailureEventArgs args)
        {
            string reason = string.IsNullOrEmpty(args.Reason) ? "Unknown reason" : args.Reason;
            AppendLog($"[Safety] CRITICAL FAILURE | {reason} [{args.ViolationCount} in {args.WindowSeconds}s]");
        }

        private void OnSafetyError(SafetyErrorEventArgs args)
        {
            string source = string.IsNullOrEmpty(args.Source) ? "Unknown source" : args.Source;
            string message = string.IsNullOrEmpty(args.Message) ? "No message" : args.Message;
            string details = string.IsNullOrEmpty(args.Details) ? "-" : args.Details;
            AppendLog($"[Safety] ERROR {source}: {message} ({details})");
        }

        private void AppendLog(string message)
        {
            lock (_entries)
            {
                _entries.Enqueue(message);
                _allLogs.AppendLine(message);

                if (_entries.Count > maxLines)
                {
                    _entries.Dequeue();
                    RebuildDisplayBuilder();
                }
                else
                {
                    if (_displayBuilder.Length > 0)
                    {
                        _displayBuilder.Append('\n');
                    }
                    _displayBuilder.Append(message);
                }
            }

            RefreshDisplay();
        }

        private void RebuildDisplayBuilder()
        {
            _displayBuilder.Clear();
            bool first = true;
            foreach (var entry in _entries)
            {
                if (!first)
                    _displayBuilder.Append('\n');
                _displayBuilder.Append(entry);
                first = false;
            }
        }

        private void RefreshDisplay()
        {
            if (logText == null)
            {
                return;
            }

            lock (_entries)
            {
                logText.SetText(_displayBuilder);
            }
            Canvas.ForceUpdateCanvases();
        }

        public string GetFullLog() => _allLogs.ToString();
        public void ClearLog()
        {
            lock (_entries)
            {
                _entries.Clear();
                _allLogs.Clear();
            }
            RefreshDisplay();
        }
    }
}
