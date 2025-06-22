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
    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;

    // Internal log entry
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
        if (eventBus == null)
        {
            Debug.LogError("ComprehensiveSessionLogger: EventBus not assigned.", this);
            enabled = false;
            return;
        }

        // Subscribe to all events
        eventBus.onSessionStarted.AddListener(OnSessionStarted);
        eventBus.onSessionPaused.AddListener(OnSessionPaused);
        eventBus.onSessionResumed.AddListener(OnSessionResumed);
        eventBus.onSessionCompleted.AddListener(OnSessionCompleted);

        eventBus.onActionAttempt.AddListener(OnActionAttempt);
        eventBus.onPpeStateChanged.AddListener(OnPpeStateChanged);
        eventBus.onTaskStarted.AddListener(OnTaskStarted);
        eventBus.onTaskCompleted.AddListener(OnTaskCompleted);
        eventBus.onTaskTimeout.AddListener(OnTaskTimeout);
        eventBus.onScoreChanged.AddListener(OnScoreChanged);
        eventBus.onGroupStarted.AddListener(OnGroupStarted);
        eventBus.onGroupCompleted.AddListener(OnGroupCompleted);
    }

    private void OnDestroy()
    {
        if (eventBus == null) return;

        // Unsubscribe
        eventBus.onSessionStarted.RemoveListener(OnSessionStarted);
        eventBus.onSessionPaused.RemoveListener(OnSessionPaused);
        eventBus.onSessionResumed.RemoveListener(OnSessionResumed);
        eventBus.onSessionCompleted.RemoveListener(OnSessionCompleted);

        eventBus.onActionAttempt.RemoveListener(OnActionAttempt);
        eventBus.onPpeStateChanged.RemoveListener(OnPpeStateChanged);
        eventBus.onTaskStarted.RemoveListener(OnTaskStarted);
        eventBus.onTaskCompleted.RemoveListener(OnTaskCompleted);
        eventBus.onTaskTimeout.RemoveListener(OnTaskTimeout);
        eventBus.onScoreChanged.RemoveListener(OnScoreChanged);
        eventBus.onGroupStarted.RemoveListener(OnGroupStarted);
        eventBus.onGroupCompleted.RemoveListener(OnGroupCompleted);
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