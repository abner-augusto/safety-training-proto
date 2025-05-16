using System;
using System.Collections.Generic;

// Reasons for a score change
public enum ScoreReason
{
    Success,
    IncorrectAction,
    MissingPPE,
    Timeout,
    // Add more reasons
}

// Represents a single event that changed the score
[System.Serializable] // Make it serializable for JSON logging
public class ScoreEntry
{
    public DateTime timestamp;
    public string taskName;
    public int pointsChange;
    public ScoreReason reason;
    public string details; // Optional additional info

    public ScoreEntry(string taskName, int pointsChange, ScoreReason reason, string details = "")
    {
        this.timestamp = DateTime.Now;
        this.taskName = taskName;
        this.pointsChange = pointsChange;
        this.reason = reason;
        this.details = details;
    }

    public override string ToString()
    {
        return $"{timestamp:HH:mm:ss} - Task: '{taskName}' | Points: {pointsChange} | Reason: {reason} | Details: {details}";
    }
}

// Helper class for JSON serialization of a list
[System.Serializable]
public class ScoreEntryListWrapper
{
    public List<ScoreEntry> entries;

    public ScoreEntryListWrapper(List<ScoreEntry> entries)
    {
        this.entries = entries;
    }
}