using System.Collections.Generic;
using SafetyProto.Data.Enums;
using UnityEngine;

namespace SafetyProto.Data.ScriptableObjects
{
    [CreateAssetMenu(fileName = "NewSafetyTask", menuName = "VRSafetyTraining/SafetyTask", order = 1)]
    public class SafetyTask : ScriptableObject
    {
        [Header("Task Details")]
        public string taskName = "Untitled Task";
        [TextArea(3, 5)]
        public string taskDescription = "Describe the task objectives here.";

        [Header("Task Logic")]
        public ActionType expectedAction = ActionType.None;
        
        [Header("Scoring")]
        public int successPoints = 100;
        public int failurePenalty; // Penalty for wrong action or timeout
        public int ppePenalty;    // Additional penalty if PPE is missing during an action

        [Header("Requirements")]
        public List<PPEType> requiredPPE = new List<PPEType>();
    }
}
