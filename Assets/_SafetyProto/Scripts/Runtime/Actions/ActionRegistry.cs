using System;
using System.Collections.Generic;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using UnityEngine;

namespace SafetyProto.Runtime.Actions
{
    [CreateAssetMenu(fileName = "ActionRegistry", menuName = "SafetyProto/Actions/ActionRegistry", order = 1)]
    public class ActionRegistry : ScriptableObject
    {
        [SerializeField] private List<ActionTypeSO> actions = new List<ActionTypeSO>();

        private readonly Dictionary<string, ActionTypeSO> _lookup = new Dictionary<string, ActionTypeSO>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ActionTypeSO> Actions => actions;

        private void OnEnable() => RebuildLookup();

        private void OnValidate()
        {
            ValidateEntries();
            RebuildLookup();
        }

        public bool TryGet(string actionId, out ActionTypeSO action)
        {
            action = null;
            if (string.IsNullOrWhiteSpace(actionId)) return false;

            EnsureLookupIsReady();
            return _lookup.TryGetValue(NormalizeId(actionId), out action);
        }

        public ActionTypeSO GetOrThrow(string actionId)
        {
            if (TryGet(actionId, out var action)) return action;
            throw new KeyNotFoundException($"Action '{actionId}' is not registered in {name}.");
        }

        private void ValidateEntries()
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < actions.Count; i++)
            {
                var entry = actions[i];

                if (entry == null)
                {
                    SafetyLog.Error($"[ActionRegistry] Null entry at index {i} in registry '{name}'.", this);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.ActionId))
                {
                    SafetyLog.Error($"[ActionRegistry] Action '{entry.name}' has an empty ActionId in registry '{name}'.", entry);
                    continue;
                }

                var normalized = NormalizeId(entry.ActionId);
                if (!seenIds.Add(normalized))
                {
                    SafetyLog.Error($"[ActionRegistry] Duplicate ActionId '{normalized}' detected in registry '{name}'.", this);
                }
            }
        }

        private void RebuildLookup()
        {
            _lookup.Clear();

            foreach (var action in actions)
            {
                if (action == null || string.IsNullOrWhiteSpace(action.ActionId)) continue;

                var normalized = NormalizeId(action.ActionId);
                if (_lookup.ContainsKey(normalized)) continue;

                _lookup.Add(normalized, action);
            }
        }

        private void EnsureLookupIsReady()
        {
            if (_lookup.Count == 0 && actions.Count > 0)
            {
                RebuildLookup();
            }
        }

        private static string NormalizeId(string rawId) => rawId.Trim();
    }
}
