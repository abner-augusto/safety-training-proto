using System.Collections.Generic;
using System.Text;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using UnityEngine;
using TMPro;

namespace SafetyProto.UI
{
    public class LogHUD : MonoBehaviour
    {
        [Tooltip("Maximum number of messages to keep in the log.")]
        public int maxLines = 20;
        [SerializeField] private TextMeshProUGUI logText;
        [SerializeField] private bool showOnlyProjectLogs = true;

        private struct LogEntry { public string Message; public bool IsProjectLog; }
        private readonly Queue<LogEntry> _entries = new();
        private readonly StringBuilder _allLogs = new();

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void Start()
        {
            if (logText == null)
            {
                SafetyLog.Error("LogHUD: logText is null! Please assign it in the inspector.", this);
                enabled = false;
                return;
            }
            logText.text = "LogHUD Active";
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            bool isProj = !string.IsNullOrEmpty(stackTrace) && stackTrace.Contains(GameConstants.Logging.ProjectIdentifier);
            string msg = $"{condition}";

            lock (_entries)
            {
                _entries.Enqueue(new LogEntry { Message = msg, IsProjectLog = isProj });
                _allLogs.AppendLine(msg);
                while (_entries.Count > maxLines)
                    _entries.Dequeue();
            }
        }

        private void Update()
        {
            lock (_entries)
            {
                var display = new List<string>();
                foreach (var e in _entries)
                    if (!showOnlyProjectLogs || e.IsProjectLog)
                        display.Add(e.Message);
                logText.text = string.Join("\n", display);
            }
            Canvas.ForceUpdateCanvases();
        }

        public void ToggleLogFilter(bool onlyMyCode) => showOnlyProjectLogs = onlyMyCode;
        public string GetFullLog() => _allLogs.ToString();
        public void ClearLog()
        {
            lock (_entries)
            {
                _entries.Clear();
                _allLogs.Clear();
                logText.text = "";
            }
        }
    }
}
