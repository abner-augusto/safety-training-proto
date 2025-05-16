using UnityEngine;
using System.Collections.Generic;
using System.IO; // Needed for File operations
using System; // Needed for DateTime
// JsonUtility requires UnityEngine, but System.Text.Json is often preferred for more complex scenarios.
// We'll stick to JsonUtility as it's simpler for this use case and built-in.

// Records various events with timestamps (Placeholder for local JSON storage)
public class DataLogger : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static DataLogger Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Don't destroy on load if you need logs across scenes
            // DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    // --- End Singleton Pattern ---

    private List<ScoreEntry> scoreLog = new List<ScoreEntry>();
    // Could add other log types (e.g., List<TaskEvent>, List<PPEEvent>)

    // Define where to save the log file
    private string logFileName = "training_log.json";
    private string logFilePath;

    private void Start()
    {
        // Application.persistentDataPath is a good place for local data storage
        logFilePath = Path.Combine(Application.persistentDataPath, logFileName);
        Debug.Log($"DataLogger: Log file path set to {logFilePath}");

        // Load existing log on startup
        LoadLog();
    }

    public void LogScoreEvent(ScoreEntry entry)
    {
        scoreLog.Add(entry);
        Debug.Log($"DataLogger: Logged Score Event: {entry}");
        // Optional: Save immediately after each log (less efficient)
        // SaveLog();
    }

    // Add methods for other log types if needed
    // public void LogTaskEvent(...) { ... }
    // public void LogPPECheck(...) { ... }

    // Saves the current log data to disk
    public void SaveLog()
    {
        Debug.Log("DataLogger: Saving log...");
        try
        {
            // JsonUtility requires a root object, so wrap the list
            ScoreEntryListWrapper wrapper = new ScoreEntryListWrapper(scoreLog);
            string json = JsonUtility.ToJson(wrapper, true); // true for pretty printing

            File.WriteAllText(logFilePath, json);
            Debug.Log($"DataLogger: Log saved to {logFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"DataLogger: Failed to save log: {e.Message}");
        }
    }

    // Loads log data from disk
    public void LoadLog()
    {
        Debug.Log("DataLogger: Loading log...");
        if (File.Exists(logFilePath))
        {
            try
            {
                string json = File.ReadAllText(logFilePath);
                ScoreEntryListWrapper wrapper = JsonUtility.FromJson<ScoreEntryListWrapper>(json);

                if (wrapper != null && wrapper.entries != null)
                {
                    scoreLog = wrapper.entries;
                    Debug.Log($"DataLogger: Log loaded successfully with {scoreLog.Count} entries.");
                    // Optional: Print loaded entries
                    // foreach(var entry in scoreLog) { Debug.Log($"Loaded: {entry}"); }
                }
                else
                {
                    Debug.LogWarning("DataLogger: Loaded JSON was empty or invalid format.");
                    scoreLog = new List<ScoreEntry>(); // Start fresh if invalid
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"DataLogger: Failed to load log: {e.Message}");
                scoreLog = new List<ScoreEntry>(); // Start fresh on error
            }
        }
        else
        {
            Debug.Log("DataLogger: No existing log file found.");
            scoreLog = new List<ScoreEntry>(); // Start with an empty log
        }
    }

    // Clears the current log data in memory (does not delete file unless saved)
    public void ClearLogInMemory()
    {
        scoreLog.Clear();
        Debug.Log("DataLogger: Log cleared from memory.");
    }

    // Call SaveLog when the application quits or finishes a training session
    private void OnApplicationQuit()
    {
        SaveLog();
    }

    // Public access to the log (read-only)
    public List<ScoreEntry> GetScoreLog()
    {
        return new List<ScoreEntry>(scoreLog); // Return a copy to prevent external modification
    }
}