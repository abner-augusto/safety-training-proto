#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Scenarios
{
    /// <summary>
    /// JSON-backed, engine-independent <see cref="ISafetyTask"/>. Replaces both the
    /// Unity <c>SafetyTask</c> ScriptableObject (as the runtime source of truth) and
    /// the CLI harness's old <c>InMemorySafetyTask</c>. One model, all hosts.
    /// </summary>
    /// <remarks>
    /// Raw string fields (<see cref="RequiredPpeNames"/>) come straight from JSON and
    /// are validated/converted by <see cref="ScenarioLoader"/> via <see cref="Bind"/>.
    /// The typed <see cref="ISafetyTask.requiredPPE"/> is only valid after binding.
    /// </remarks>
    public sealed class SafetyTaskDef : ISafetyTask
    {
        [JsonProperty("name")]
        public string taskName { get; set; } = "unnamed";

        [JsonProperty("taskDescription")]
        public string taskDescription { get; set; } = string.Empty;

        [JsonProperty("actionId")]
        public string ActionId { get; set; } = string.Empty;

        [JsonProperty("successPoints")]
        public int successPoints { get; set; } = 100;

        [JsonProperty("failurePenalty")]
        public int failurePenalty { get; set; }

        [JsonProperty("ppePenalty")]
        public int ppePenalty { get; set; } = 20;

        /// <summary>Raw PPE names as authored in JSON (e.g. "Boots"). Bound to enums by the loader.</summary>
        [JsonProperty("requiredPPE")]
        public List<string> RequiredPpeNames { get; set; } = new();

        [JsonProperty("hintText")]
        public string hintText { get; set; } = string.Empty;

        [JsonProperty("failureAdvice")]
        public string failureAdvice { get; set; } = string.Empty;

        [JsonProperty("ppeAdvice")]
        public string ppeAdvice { get; set; } = string.Empty;

        [JsonIgnore]
        private readonly List<PPEType> _requiredPpe = new();

        [JsonIgnore]
        IReadOnlyList<PPEType> ISafetyTask.requiredPPE => _requiredPpe;

        public string ResolveExpectedActionId() =>
            string.IsNullOrWhiteSpace(ActionId) ? string.Empty : ActionId.Trim();

        /// <summary>
        /// Converts <see cref="RequiredPpeNames"/> into typed PPE values, appending a
        /// human-readable message to <paramref name="errors"/> for any unknown name.
        /// Called once by <see cref="ScenarioLoader"/> after deserialization.
        /// </summary>
        internal void Bind(string groupName, List<string> errors)
        {
            _requiredPpe.Clear();
            foreach (var name in RequiredPpeNames)
            {
                if (System.Enum.TryParse<PPEType>(name, ignoreCase: true, out var ppe))
                {
                    _requiredPpe.Add(ppe);
                }
                else
                {
                    var valid = string.Join(", ", System.Enum.GetNames(typeof(PPEType)));
                    errors.Add(
                        $"Tipo de EPI desconhecido '{name}' na tarefa '{taskName}' (grupo '{groupName}'). " +
                        $"Valores válidos: {valid}");
                }
            }
        }
    }
}
