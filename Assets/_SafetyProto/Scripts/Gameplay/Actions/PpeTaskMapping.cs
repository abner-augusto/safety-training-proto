using System;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Data.Enums;
using SafetyProto.Gameplay.PPE;
using SafetyProto.Gameplay.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    [Serializable]
    public class PpeTaskMappingEntry
    {
        public string name;                       // just for inspector readability
        public ActionType actionToTrigger;        // must match SafetyTask.expectedAction
        public List<PPEType> requiredPpe = new(); // which PPE must be valid
    }

    /// <summary>
    /// For each mapping, when all its required PPE are compliant and there is
    /// a pending task with that ActionType, emits an ActionAttempt so the
    /// SafetyRuleEngine can complete the task normally.
    /// </summary>
    public class PpeTaskMapping : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PPEManager ppeManager;
        [SerializeField] private TaskManager taskManager;
        [SerializeField] private Transform playerTransform;

        [Header("Mappings")]
        [SerializeField] private List<PpeTaskMappingEntry> mappings = new();

        [Header("Misc")]
        [SerializeField] private int interactorId = 0;

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            if (ppeManager == null)
                ppeManager = FindFirst<PPEManager>();

            if (taskManager == null)
                taskManager = FindFirst<TaskManager>();

            if (playerTransform == null && Camera.main != null)
                playerTransform = Camera.main.transform;

            if (ppeManager == null || taskManager == null)
            {
                enabled = false;
                return;
            }

            EventBus.Instance.onPpeStateChanged.AddListener(OnPpeStateChanged);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onPpeStateChanged.RemoveListener(OnPpeStateChanged);
            }
        }

        private void OnPpeStateChanged(PPEStateChangedEventArgs _)
        {
            if (mappings == null || mappings.Count == 0)
                return;

            foreach (var mapping in mappings)
            {
                if (mapping == null || mapping.actionToTrigger == ActionType.None)
                    continue;

                // 1) Are all PPE in this mapping compliant?
                if (!ppeManager.VerifyPPECompliance(mapping.requiredPpe))
                    continue;

                // 2) Is there a pending task with this action type in the CURRENT group?
                var pending = taskManager.FindPendingTaskByAction(mapping.actionToTrigger);
                if (pending == null)
                    continue;

                // 3) Emit ActionAttempt so SafetyRuleEngine handles everything
                var pos = playerTransform != null ? playerTransform.position : transform.position;
                var args = new ActionAttemptEventArgs(mapping.actionToTrigger, interactorId, pos);
                ActionEvents.RaiseActionAttempt(args);
            }
        }

#if UNITY_2023_1_OR_NEWER
        private static T FindFirst<T>() where T : UnityEngine.Object => FindFirstObjectByType<T>();
#else
        private static T FindFirst<T>() where T : UnityEngine.Object => FindObjectOfType<T>();
#endif
    }
}
