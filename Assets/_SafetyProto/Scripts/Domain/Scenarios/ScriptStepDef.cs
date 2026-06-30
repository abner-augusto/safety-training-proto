#nullable enable
using Newtonsoft.Json;

namespace SafetyProto.Domain.Scenarios
{
    /// <summary>
    /// A single scripted-input step in a scenario. Used by the CLI harness (and
    /// automated tests) to replay a canned playthrough. Ignored by the Unity
    /// runtime, which receives input from the player. Engine-independent.
    /// </summary>
    public sealed class ScriptStepDef
    {
        [JsonProperty("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonProperty("ppeType")]
        public string? PpeType { get; set; }

        [JsonProperty("isWearing")]
        public bool IsWearing { get; set; }

        [JsonProperty("actionId")]
        public string? ActionId { get; set; }

        [JsonProperty("delayMs")]
        public int DelayMs { get; set; } = 100;
    }
}
