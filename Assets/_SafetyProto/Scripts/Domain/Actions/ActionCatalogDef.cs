#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SafetyProto.Domain.Actions
{
    /// <summary>
    /// The set of action types a build understands, as a JSON document. Runtime source of
    /// truth for action existence/metadata. Case-insensitive lookup keeps action ids forgiving
    /// across hand-authored JSON and desktop-authored scenarios.
    /// </summary>
    public sealed class ActionCatalogDef
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "1";

        [JsonProperty("actions")]
        public List<ActionDef> Actions { get; set; } = new();

        [JsonIgnore]
        private Dictionary<string, ActionDef>? _lookup;

        /// <summary>Case-insensitive resolve by action id. Builds the index on first use.</summary>
        public bool TryGet(string actionId, out ActionDef? action)
        {
            action = null;
            if (string.IsNullOrWhiteSpace(actionId)) return false;

            EnsureLookup();
            return _lookup!.TryGetValue(actionId.Trim(), out action);
        }

        public bool Contains(string actionId) => TryGet(actionId, out _);

        private void EnsureLookup()
        {
            if (_lookup != null) return;

            _lookup = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in Actions)
            {
                if (action == null || string.IsNullOrWhiteSpace(action.ActionId)) continue;
                var key = action.ActionId.Trim();
                if (!_lookup.ContainsKey(key)) _lookup[key] = action;
            }
        }
    }
}
