using System;
using System.Collections.Generic;
using UnityEngine;

namespace SafetyProto.Gameplay.Actions
{
    [CreateAssetMenu(fileName = "ActionRegistry", menuName = "SafetyProto/Actions/ActionRegistry", order = 1)]
    public class ActionRegistry : ScriptableObject
    {
        [SerializeField] private List<ActionDefinition> actions = new List<ActionDefinition>();

        private readonly Dictionary<string, ActionDefinition> _lookup = new Dictionary<string, ActionDefinition>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ActionDefinition> Actions => actions;

        private void OnEnable()
        {
            RebuildLookup();
        }

        private void OnValidate()
        {
            ValidateEntries();
            RebuildLookup();
        }

        public bool TryGet(string actionId, out ActionDefinition action)
        {
            action = null;

            if (string.IsNullOrWhiteSpace(actionId))
            {
                return false;
            }

            EnsureLookupIsReady();
            return _lookup.TryGetValue(NormalizeId(actionId), out action);
        }

        public ActionDefinition GetOrThrow(string actionId)
        {
            if (TryGet(actionId, out var action))
            {
                return action;
            }

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
                    Debug.LogError($"[ActionRegistry] Null entry detected at index {i} in registry '{name}'.", this);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.ActionId))
                {
                    Debug.LogError($"[ActionRegistry] Action '{entry.name}' has an empty ActionId in registry '{name}'.", entry);
                    continue;
                }

                var normalized = NormalizeId(entry.ActionId);
                if (!seenIds.Add(normalized))
                {
                    Debug.LogError($"[ActionRegistry] Duplicate ActionId '{normalized}' detected in registry '{name}'.", this);
                }
            }
        }

        private void RebuildLookup()
        {
            _lookup.Clear();

            foreach (var action in actions)
            {
                if (action == null || string.IsNullOrWhiteSpace(action.ActionId))
                {
                    continue;
                }

                var normalized = NormalizeId(action.ActionId);

                if (_lookup.ContainsKey(normalized))
                {
                    continue;
                }

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

        private static string NormalizeId(string rawId)
        {
            return rawId.Trim();
        }
    }
}
