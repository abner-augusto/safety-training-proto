using System;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Runtime.PPE;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Actions
{
    [Serializable]
    public class PpeTaskMappingEntry
    {
        public string name;
        public ActionTypeSO action;
        public string actionIdOverride;

        [Tooltip("The PPE type that triggers this mapping when equipped. " +
                 "When this PPE is detected as worn, the action attempt is emitted.")]
        public PPEType equipPpe = PPEType.None;

        [Tooltip("Optional additional PPE types that must ALSO be worn for this mapping to fire. " +
                 "Leave empty if the equip action itself is the only requirement.")]
        public List<PPEType> additionalRequiredPpe = new();

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

        private readonly HashSet<string> _compliantActionIds = new HashSet<string>();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (mappings == null) return;
            foreach (var entry in mappings)
            {
                if (entry == null) continue;
                if (entry.equipPpe != PPEType.None) continue;
                entry.equipPpe = InferPpeTypeFromName(entry.name);
            }
        }

        private static PPEType InferPpeTypeFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return PPEType.None;
            var lower = name.ToLowerInvariant();
            if (lower.Contains("helmet") || lower.Contains("capacete")) return PPEType.Helmet;
            if (lower.Contains("glove") || lower.Contains("luva")) return PPEType.Gloves;
            if (lower.Contains("goggle") || lower.Contains("oculo")) return PPEType.Goggles;
            if (lower.Contains("harness") || lower.Contains("cinto") || lower.Contains("talabarte")) return PPEType.Harness;
            if (lower.Contains("vest") || lower.Contains("colete")) return PPEType.Vest;
            if (lower.Contains("boot") || lower.Contains("bota") || lower.Contains("calcado")) return PPEType.Boots;
            return PPEType.None;
        }
#endif

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
            _compliantActionIds.Clear();
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

                if (mapping.equipPpe == PPEType.None)
                    continue;

                bool isEquipped = ppeManager.IsWearing(mapping.equipPpe);
                bool additionalOk = AreAllWorn(ppeManager, mapping.additionalRequiredPpe);
                bool isCompliant = isEquipped && additionalOk;

                if (isCompliant && _compliantActionIds.Add(actionId))
                {
                    var pending = taskManager.FindPendingTaskByActionId(actionId);
                    if (pending == null)
                    {
                        _compliantActionIds.Remove(actionId);
                        continue;
                    }

                    var position = playerTransform != null ? playerTransform.position : transform.position;
                    var sourceId = string.IsNullOrWhiteSpace(mapping.name) ? nameof(PpeTaskMapping) : mapping.name.Trim();
                    ActionEvents.PublishActionAttempt(actionId, sourceId, "ppe_mapping", position, interactorId);
                }
                else if (!isCompliant)
                {
                    _compliantActionIds.Remove(actionId);
                }
            }
        }

#if UNITY_2023_1_OR_NEWER
        private static T FindFirst<T>() where T : UnityEngine.Object => FindFirstObjectByType<T>();
#else
        private static T FindFirst<T>() where T : UnityEngine.Object => FindObjectOfType<T>();
#endif

        private static bool AreAllWorn(PPEManager mgr, List<PPEType> types)
        {
            if (types == null || types.Count == 0) return true;
            foreach (var t in types)
            {
                if (!mgr.IsWearing(t)) return false;
            }
            return true;
        }
    }
}
