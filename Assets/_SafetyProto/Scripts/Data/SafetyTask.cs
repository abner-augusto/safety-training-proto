using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewSafetyTask", menuName = "VRSafetyTraining/SafetyTask", order = 1)]
public class SafetyTask : ScriptableObject
{
    [Header("Task Details")]
    public string taskName = "Untitled Task";
    [TextArea(3, 5)]
    public string taskDescription = "Describe the task objectives here.";

    [Header("Task Logic")]
    public ActionType expectedAction = ActionType.None;
    public float timeLimit = 60f; // in seconds
    
    [Header("Scoring")]
    public int successPoints = 100;
    public int failurePenalty = 50; // Penalty for wrong action or timeout
    public int ppePenalty = 25;    // Additional penalty if PPE is missing during an action

    [Header("Requirements")]
    public List<PPEType> requiredPPE = new List<PPEType>();
}