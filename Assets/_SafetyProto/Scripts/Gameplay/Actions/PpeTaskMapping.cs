using System;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Data.Enums;
using SafetyProto.Gameplay.Actions;
using SafetyProto.Gameplay.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    [Serializable]
    public class PpeTaskMappingEntry
    {
        public string name;
        public ActionDefinition action;
        public string actionIdOverride;
        public List<PPEType> requiredPpe = new();

        public string ResolveActionId()
        {
            if (action != null && !string.IsNullOrWhiteSpace(action.ActionId))
            {
                actionIdOverride = action.ActionId;
                return action.ActionId;
            }

            return string.IsNullOrWhiteSpace(actionIdOverride) ? string.Empty : actionIdOverride.Trim();
        }
    }

    /// <summary>
    /// When PPE becomes compliant for a mapping and that action is pending in the current task group,
    /// emits an action attempt so the normal task flow can complete.
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
                if (mapping == null)
                    continue;

                var actionId = mapping.ResolveActionId();
                if (string.IsNullOrEmpty(actionId))
                    continue;

                if (!ppeManager.VerifyPPECompliance(mapping.requiredPpe))
                    continue;

                var pending = taskManager.FindPendingTaskByActionId(actionId);
                if (pending == null)
                    continue;

                var position = playerTransform != null ? playerTransform.position : transform.position;
                var sourceId = string.IsNullOrWhiteSpace(mapping.name) ? nameof(PpeTaskMapping) : mapping.name.Trim();
                ActionEvents.PublishActionAttempt(actionId, sourceId, "ppe_mapping", position, interactorId);
            }
        }

#if UNITY_2023_1_OR_NEWER
        private static T FindFirst<T>() where T : UnityEngine.Object => FindFirstObjectByType<T>();
#else
        private static T FindFirst<T>() where T : UnityEngine.Object => FindObjectOfType<T>();
#endif
    }
}
