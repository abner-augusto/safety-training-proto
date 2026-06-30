#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SafetyProto.Domain.Scenarios
{
    /// <summary>
    /// Outcome of a <see cref="ScenarioLoader.Parse"/> call. Never throws into the
    /// runtime: callers inspect <see cref="Success"/> and fall back as needed.
    /// </summary>
    public sealed class ScenarioLoadResult
    {
        public bool Success { get; }
        public ScenarioDef? Scenario { get; }
        public IReadOnlyList<string> Errors { get; }

        private ScenarioLoadResult(bool success, ScenarioDef? scenario, IReadOnlyList<string> errors)
        {
            Success = success;
            Scenario = scenario;
            Errors = errors;
        }

        internal static ScenarioLoadResult Ok(ScenarioDef scenario) =>
            new(true, scenario, System.Array.Empty<string>());

        internal static ScenarioLoadResult Fail(IReadOnlyList<string> errors) =>
            new(false, null, errors);

        internal static ScenarioLoadResult Fail(string error) =>
            new(false, null, new[] { error });

        /// <summary>Single-line, human-readable join of all errors (for logging).</summary>
        public string ErrorSummary => string.Join(" | ", Errors);
    }

    /// <summary>
    /// Single source of truth for parsing and validating a scenario JSON document.
    /// Shared by every host (Unity runtime, CLI harness, desktop authoring app) so
    /// the same input is accepted or rejected identically everywhere.
    /// </summary>
    public static class ScenarioLoader
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>
        /// Deserializes and validates a scenario document. On any structural or
        /// semantic problem, returns a failing result with human-readable messages
        /// instead of throwing.
        /// </summary>
        public static ScenarioLoadResult Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return ScenarioLoadResult.Fail("JSON de cenário vazio.");
            }

            ScenarioDef? scenario;
            try
            {
                scenario = JsonConvert.DeserializeObject<ScenarioDef>(json, Settings);
            }
            catch (JsonException ex)
            {
                return ScenarioLoadResult.Fail($"Falha ao interpretar o JSON do cenário: {ex.Message}");
            }

            if (scenario == null)
            {
                return ScenarioLoadResult.Fail("JSON de cenário resultou em documento nulo.");
            }

            var errors = new List<string>();

            // Pass 1: bind execution modes and PPE on every group/task.
            foreach (var group in scenario.Groups)
            {
                group.Bind(errors);
            }

            // Pass 2: resolve requiredGroups by name (after all groups exist).
            var byName = new Dictionary<string, TaskGroupDef>();
            foreach (var group in scenario.Groups)
            {
                byName[group.groupName] = group;
            }

            foreach (var group in scenario.Groups)
            {
                foreach (var reqName in group.RequiredGroupNames)
                {
                    if (byName.TryGetValue(reqName, out var reqGroup))
                    {
                        group.AddRequiredGroup(reqGroup);
                    }
                    else
                    {
                        errors.Add(
                            $"Grupo requerido '{reqName}' (referenciado por '{group.groupName}') não existe no cenário.");
                    }
                }
            }

            return errors.Count > 0
                ? ScenarioLoadResult.Fail(errors)
                : ScenarioLoadResult.Ok(scenario);
        }
    }
}
