#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SafetyProto.Domain.Actions
{
    /// <summary>
    /// Engine-independent, JSON-backed <em>logical</em> definition of an action type.
    /// This is the source of truth the runtime resolves against (via ActionResolver) and
    /// the desktop authoring app validates against — everything that is <em>not</em>
    /// presentation. Presentation (icon, colour, SFX, haptics) stays Unity-side in the
    /// ActionTypeSO asset, keyed by <see cref="ActionId"/>; splitting the two is the whole
    /// point of this model. Serialized fields mirror the logical half of ActionTypeSO so a
    /// one-shot bake preserves the curated metadata exactly.
    /// </summary>
    public sealed class ActionDef
    {
        /// <summary>Stable, case-insensitive lookup key (e.g. "connect_harness").</summary>
        [JsonProperty("actionId")]
        public string ActionId { get; set; } = string.Empty;

        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>Category name (ActionTypeSO.ActionCategory), stored as a string for
        /// human-editable JSON — mirrors how PPE/executionMode are serialized by name.</summary>
        [JsonProperty("category")]
        public string Category { get; set; } = string.Empty;

        /// <summary>Effective telemetry name (already resolved: override, else actionId).</summary>
        [JsonProperty("telemetryName")]
        public string TelemetryName { get; set; } = string.Empty;

        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonProperty("regulatoryRefs")]
        public List<string> RegulatoryRefs { get; set; } = new();

        [JsonProperty("isSafetyCritical")]
        public bool IsSafetyCritical { get; set; }

        [JsonProperty("isHiddenInUI")]
        public bool IsHiddenInUI { get; set; }

        [JsonProperty("cooldownSeconds")]
        public float CooldownSeconds { get; set; }

        [JsonProperty("expectedDurationSeconds")]
        public float ExpectedDurationSeconds { get; set; }

        [JsonProperty("baseScore")]
        public int BaseScore { get; set; }

        [JsonProperty("severity")]
        public int Severity { get; set; }

        public override string ToString() =>
            string.IsNullOrWhiteSpace(DisplayName) ? ActionId : DisplayName;
    }
}
