using UnityEngine;
using System.Collections.Generic; // Needed for List

// Defines a single task for the player to complete
[CreateAssetMenu(fileName = "NewTaskDef", menuName = "SafetyTraining/Task Definition", order = 1)]
public class TaskDef : ScriptableObject
{
    [Header("Task Description")]
    public string taskName = "New Task";
    [TextArea]
    public string taskDescription = "Perform the necessary actions to complete this task.";

    [Header("Expected Actions")]
    public ActionType expectedAction = ActionType.None;
    // Potentially add a list of required actions for complex tasks

    [Header("Time Limit")]
    public float timeLimit = 30f; // Seconds

    [Header("Scoring")]
    public int successPoints = 100;
    public int failurePenalty = -50;
    public int ppePenalty = -30; // Penalty for missing PPE even if action is correct

    [Header("Requirements")]
    public List<ProtectionType> requiredPPE;
    // Add other requirements like required tools, etc.
}