#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SafetyProto.Domain.Scenarios
{
    /// <summary>
    /// Root of a unified training-scenario document. Loaded identically by the Unity
    /// runtime, the CLI harness, and the desktop authoring app via
    /// <see cref="ScenarioLoader"/>. Engine-independent.
    /// </summary>
    public sealed class ScenarioDef
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "unnamed";

        [JsonProperty("participantId")]
        public string ParticipantId { get; set; } = "P000";

        [JsonProperty("groups")]
        public List<TaskGroupDef> Groups { get; set; } = new();

        /// <summary>Optional scripted playthrough; consumed by the CLI harness, ignored by Unity.</summary>
        [JsonProperty("script")]
        public List<ScriptStepDef> Script { get; set; } = new();
    }
}
