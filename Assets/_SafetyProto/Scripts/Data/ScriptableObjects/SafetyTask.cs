using System.Collections.Generic;
using SafetyProto.Data.Enums;
using UnityEngine;
using SafetyProto.Gameplay.Actions;

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
        public ActionDefinition expectedAction;
        [Tooltip("Auto-filled from ExpectedAction; used as a fallback identifier when the asset reference is missing.")]
        public string expectedActionId = string.Empty;
        
        [Header("Scoring")]
        public int successPoints = 100;
        public int failurePenalty; // Penalty for wrong action or timeout
        public int ppePenalty;    // Additional penalty if PPE is missing during an action

        [Header("Requirements")]
        public List<PPEType> requiredPPE = new List<PPEType>();

        [Header("Guidance")]
        [TextArea(2, 4)]
        public string hintText = "Dica rápida para o participante.";

        public string ResolveExpectedActionId()
        {
            if (expectedAction != null && !string.IsNullOrWhiteSpace(expectedAction.ActionId))
            {
                expectedActionId = expectedAction.ActionId;
                return expectedAction.ActionId;
            }

            return string.IsNullOrWhiteSpace(expectedActionId) ? string.Empty : expectedActionId.Trim();
        }

        private void OnValidate()
        {
            if (expectedAction != null && !string.IsNullOrWhiteSpace(expectedAction.ActionId))
            {
                expectedActionId = expectedAction.ActionId;
            }
            else if (!string.IsNullOrEmpty(expectedActionId))
            {
                expectedActionId = expectedActionId.Trim();
            }
        }
    }
}
