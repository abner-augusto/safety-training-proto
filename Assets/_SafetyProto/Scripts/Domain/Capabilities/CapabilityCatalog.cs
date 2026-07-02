#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SafetyProto.Domain.Capabilities
{
    /// <summary>
    /// Machine-readable inventory of what a build can do: the action ids, PPE types,
    /// and scenes/phases that are actually implemented. Exported from Unity per build
    /// and consumed by the desktop authoring app so a safety specialist can only pick
    /// valid options (dropdowns, not free text) and have scenarios validated against
    /// reality before deployment. Engine-independent so the authoring app needs no
    /// Unity dependency.
    /// </summary>
    public sealed class CapabilityCatalog
    {
        /// <summary>Schema/build identifier so the authoring app can warn on mismatch.</summary>
        [JsonProperty("version")]
        public string Version { get; set; } = "1";

        /// <summary>Registered action ids from the embedded action catalog.</summary>
        [JsonProperty("actionIds")]
        public List<string> ActionIds { get; set; } = new();

        /// <summary>Available PPE type names (from the PPEType enum).</summary>
        [JsonProperty("ppeTypes")]
        public List<string> PpeTypes { get; set; } = new();

        /// <summary>Scenes/phases the build exposes (from PhaseController).</summary>
        [JsonProperty("phases")]
        public List<string> Phases { get; set; } = new();
    }
}
