using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using JetBrains.Annotations; // Added for Rider warning suppression

/// <summary>
/// Logs all relevant session events with timestamps and details,
/// then writes a comprehensive JSON diary at session end.
/// </summary>

public class SessionLogger : MonoBehaviour
{
    [Serializable]
    private class LogEntry
    {
        [UsedImplicitly] public string timestamp;
        [UsedImplicitly] public string eventName;
        [UsedImplicitly] public string details;
    }

    [Serializable]
    private class SessionSummary
    {
        [UsedImplicitly] public float totalElapsedTime;
        [UsedImplicitly] public int totalScore;
        [UsedImplicitly] public int tasksCompleted;
        [UsedImplicitly] public int totalTasks;
    }

    // Wrapper for serialization
    [Serializable]
    private class SessionLog
    {
        [UsedImplicitly] public List<LogEntry> entries = new List<LogEntry>();
        [UsedImplicitly] public SessionSummary summary;
    }

    private SessionLog _sessionLog = new SessionLog();
    private void Start()
    {
        if (!this.IsEventBusReady())
        {
            return;
        }

        // Subscribe to all events
        EventBus.Instance.onSessionStarted.AddListener(OnSessionStarted);
        EventBus.Instance.onSessionPaused.AddListener(OnSessionPaused);
        EventBus.Instance.onSessionResumed.AddListener(OnSessionResumed);
        EventBus.Instance.onSessionCompleted.AddListener(OnSessionCompleted);
        EventBus.Instance.onActionAttempt.AddListener(OnActionAttempt);
        EventBus.Instance.onPpeStateChanged.AddListener(OnPpeStateChanged);
        EventBus.Instance.onTaskStarted.AddListener(OnTaskStarted);
        EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
        EventBus.Instance.onTaskTimeout.AddListener(OnTaskTimeout);
        EventBus.Instance.onScoreChanged.AddListener(OnScoreChanged);
        EventBus.Instance.onGroupStarted.AddListener(OnGroupStarted);
        EventBus.Instance.onGroupCompleted.AddListener(OnGroupCompleted);
    }

    private void OnDestroy()
    {
        if (EventBus.Instance != null)
        {
            // Unsubscribe from all events
            EventBus.Instance.onSessionStarted.RemoveListener(OnSessionStarted);
            EventBus.Instance.onSessionPaused.RemoveListener(OnSessionPaused);
            EventBus.Instance.onSessionResumed.RemoveListener(OnSessionResumed);
            EventBus.Instance.onSessionCompleted.RemoveListener(OnSessionCompleted);
            EventBus.Instance.onActionAttempt.RemoveListener(OnActionAttempt);
            EventBus.Instance.onPpeStateChanged.RemoveListener(OnPpeStateChanged);
            EventBus.Instance.onTaskStarted.RemoveListener(OnTaskStarted);
            EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
            EventBus.Instance.onTaskTimeout.RemoveListener(OnTaskTimeout);
            EventBus.Instance.onScoreChanged.RemoveListener(OnScoreChanged);
            EventBus.Instance.onGroupStarted.RemoveListener(OnGroupStarted);
            EventBus.Instance.onGroupCompleted.RemoveListener(OnGroupCompleted);
        }
    }

    // Event handlers
    private void OnSessionStarted(SessionStartedEventArgs args) => LogEvent("SessionStarted", "");
    private void OnSessionPaused(SessionPausedEventArgs args) => LogEvent("SessionPaused", "");
    private void OnSessionResumed(SessionResumedEventArgs args) => LogEvent("SessionResumed", "");
    private void OnSessionCompleted(SessionCompletedEventArgs args)
    {
        LogEvent("SessionCompleted",
            $"Time={args.totalElapsedTime}, Score={args.totalScore}, Completed={args.tasksCompleted}/{args.totalTasks}");
        _sessionLog.summary = new SessionSummary
        {
            totalElapsedTime = args.totalElapsedTime,
            totalScore = args.totalScore,
            tasksCompleted = args.tasksCompleted,
            totalTasks = args.totalTasks
        };
        WriteLogToFile();
    }

    private void OnActionAttempt(ActionAttemptEventArgs args)
        => LogEvent("ActionAttempt", args.ActionType.ToString());
    private void OnPpeStateChanged(PPEStateChangedEventArgs args)
        => LogEvent("PpeStateChanged", $"PPE={args.PpeType}, Wearing={args.IsWearing}");
    private void OnTaskStarted(TaskEventArgs args)
        => LogEvent("TaskStarted", args.Task.taskName);
    private void OnTaskCompleted(TaskEventArgs args)
        => LogEvent("TaskCompleted", args.Task.taskName);
    private void OnTaskTimeout(TaskEventArgs args)
        => LogEvent("TaskTimeout", args.Task.taskName);
    private void OnScoreChanged(ScoreChangedEventArgs args)
        => LogEvent("ScoreChanged", $"Delta={args.Delta}, Total={args.TotalScore}");
    private void OnGroupStarted(TaskGroupEventArgs args)
        => LogEvent("GroupStarted", args.Group.groupName);
    private void OnGroupCompleted(TaskGroupEventArgs args)
        => LogEvent("GroupCompleted", args.Group.groupName);
    // Helper to add an entry
    private void LogEvent(string eventName, string details)
    {
        _sessionLog.entries.Add(new LogEntry {
            timestamp = DateTime.Now.ToString("o"),
            eventName = eventName,
            details = details
        });
    }

    // Serialize and save at end
    private void WriteLogToFile()
    {
        try
        {
            string json = JsonUtility.ToJson(_sessionLog, prettyPrint: true);
            string filename = $"session_log_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string path = Path.Combine(Application.persistentDataPath, filename);
            File.WriteAllText(path, json);
            Debug.Log($"ComprehensiveSessionLogger: Log saved to {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"ComprehensiveSessionLogger: Failed to write log. {ex.Message}");
        }
    }
}