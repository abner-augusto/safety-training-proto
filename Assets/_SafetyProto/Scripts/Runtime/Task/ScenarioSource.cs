#nullable enable
using System.IO;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scenarios;
using UnityEngine;

namespace SafetyProto.Runtime.Task
{
    /// <summary>
    /// Layered, fail-safe loader for the unified scenario JSON. Resolves a scenario in
    /// two tiers so the build is resilient yet updatable without a rebuild:
    /// <list type="number">
    ///   <item>Optional external override at
    ///   <c>Application.persistentDataPath/scenarios/{name}.json</c> (adb-pushable, or
    ///   written by the desktop authoring app). Used only if it exists AND parses.</item>
    ///   <item>Embedded default at <c>Resources/Scenarios/{name}</c>, shipped in the
    ///   build and validated at bake time. The guaranteed floor.</item>
    /// </list>
    /// Any failure (missing/corrupt override, invalid default) is logged via
    /// <see cref="SafetyLog"/> and never thrown — callers get the best available
    /// scenario or <c>null</c>.
    /// </summary>
    public static class ScenarioSource
    {
        public static string OverrideDirectory =>
            Path.Combine(Application.persistentDataPath, "scenarios");

        /// <summary>
        /// Loads the scenario named <paramref name="resourceName"/> using the layered
        /// strategy. Returns <c>null</c> only when even the embedded default is missing
        /// or invalid.
        /// </summary>
        public static ScenarioDef? Load(string resourceName)
        {
            // Tier 1: external override.
            var overridePath = Path.Combine(OverrideDirectory, resourceName + ".json");
            if (File.Exists(overridePath))
            {
                var result = ScenarioLoader.Parse(SafeReadAllText(overridePath));
                if (result.Success && result.Scenario != null)
                {
                    SafetyLog.Info($"[ScenarioSource] Cenário carregado do override externo: {overridePath}");
                    return result.Scenario;
                }

                SafetyLog.Warning(
                    $"[ScenarioSource] Override '{overridePath}' inválido, usando o cenário embarcado. " +
                    $"Motivo: {result.ErrorSummary}");
            }

            // Tier 2: embedded default.
            var textAsset = Resources.Load<TextAsset>($"Scenarios/{resourceName}");
            if (textAsset == null)
            {
                SafetyLog.Error(
                    $"[ScenarioSource] Cenário embarcado 'Resources/Scenarios/{resourceName}' não encontrado.");
                return null;
            }

            var embedded = ScenarioLoader.Parse(textAsset.text);
            if (embedded.Success && embedded.Scenario != null)
            {
                return embedded.Scenario;
            }

            SafetyLog.Error(
                $"[ScenarioSource] Cenário embarcado '{resourceName}' é inválido: {embedded.ErrorSummary}");
            return null;
        }

        private static string SafeReadAllText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                SafetyLog.Warning($"[ScenarioSource] Falha ao ler '{path}': {ex.Message}");
                return string.Empty;
            }
        }
    }
}
