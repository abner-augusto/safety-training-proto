using UnityEngine;
using System.Collections.Generic; // Needed for List

// Orchestrates the main game flow, initializes managers, starts sequences
public class GameManager : MonoBehaviour
{
    // --- Singleton Pattern ---
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // Managers usually persist
        }
        else
        {
            Debug.LogWarning("Duplicate GameManager found. Destroying the new one.");
            Destroy(gameObject);
        }
    }
    // --- End Singleton Pattern ---

    [Header("Manager References")]
    [SerializeField] private XRSessionManager xrSessionManager;
    [SerializeField] private PPEManager ppeManager;
    [SerializeField] private TaskManager taskManager;
    [SerializeField] private TimerSystem timerSystem;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private DataLogger dataLogger;

    // Assign your TaskDef ScriptableObjects here in the Inspector
    [Header("Task Definitions")]
    [SerializeField] private List<TaskDef> trainingTasks;


    void Start()
    {
        Debug.Log("GameManager: Starting up...");

        // Ensure all required managers are assigned
        CheckManagerReferences();

        // Pass tasks to TaskManager
        if (taskManager != null && trainingTasks != null)
        {
            taskManager.taskDefinitions = trainingTasks;
        }
        else if (taskManager != null)
        {
            Debug.LogWarning("GameManager: No tasks assigned to trainingTasks list!");
        }


        // --- Initial Setup Sequence ---

        // 1. Initialize XR Session
        xrSessionManager?.InitializeSession();

        // 2. Load Score/Log data (DataLogger loads automatically in Start)
        dataLogger?.LoadLog(); // Redundant if DataLogger loads in its own Start, but safe call

        // 3. Reset Score (or load previous score if desired)
        scoreManager?.ResetScore(); // Starting fresh for a new session

        // 4. Subscribe to Task completion events to know when the session ends
        if (taskManager != null)
        {
            taskManager.OnAllTasksComplete += HandleTrainingComplete;
        }

        // 5. Start the first task (This kicks off the main loop)
        taskManager?.StartTrainingSequence();

        Debug.Log("GameManager: Startup sequence finished.");
    }

    private void CheckManagerReferences()
    {
        if (xrSessionManager == null) Debug.LogError("GameManager: XRSessionManager reference is null!");
        if (ppeManager == null) Debug.LogError("GameManager: PPEManager reference is null!");
        if (taskManager == null) Debug.LogError("GameManager: TaskManager reference is null!");
        if (timerSystem == null) Debug.LogError("GameManager: TimerSystem reference is null!");
        if (scoreManager == null) Debug.LogError("GameManager: ScoreManager reference is null!");
        if (uiManager == null) Debug.LogError("GameManager: UIManager reference is null!");
        if (dataLogger == null) Debug.LogError("GameManager: DataLogger reference is null!");
    }


    private void HandleTrainingComplete()
    {
        Debug.Log("GameManager: Training session concluded.");

        // Show summary UI
        uiManager?.ShowSessionSummary();

        // Save final log
        dataLogger?.SaveLog();

        // Optional: Add logic for transitioning to a results scene, restarting, etc.
    }

    // --- Optional: Helper methods for testing ---
    // You could wire these up to UI buttons or debug inputs

    public void SimulateAction(ActionType type)
    {
        taskManager?.SimulateAction(type);
    }

    public void TogglePPE(ProtectionType type)
    {
        bool isCurrentlyWearing = ppeManager.isWearing(type);
        ppeManager?.SetWearing(type, !isCurrentlyWearing);
    }
}