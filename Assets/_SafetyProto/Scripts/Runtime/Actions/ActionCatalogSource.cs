#nullable enable
using System.IO;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Actions;
using UnityEngine;

namespace SafetyProto.Runtime.Actions
{
    /// <summary>
    /// Layered, fail-safe loader for the unified action-catalog JSON, mirroring
    /// <see cref="Task.ScenarioSource"/>:
    /// <list type="number">
    ///   <item>Optional external override at
    ///   <c>Application.persistentDataPath/actions/{name}.json</c> (adb-pushable).</item>
    ///   <item>Embedded default at <c>Resources/Actions/{name}</c>, shipped in the build.</item>
    /// </list>
    /// Any failure is logged via <see cref="SafetyLog"/> (Portuguese) and never thrown —
    /// callers get the best available catalog or <c>null</c>. Resolution is by a single
    /// fixed name; other JSONs in the override folder are ignored.
    /// </summary>
    public static class ActionCatalogSource
    {
        public const string DefaultCatalogName = "actions";

        public static string OverrideDirectory =>
            Path.Combine(Application.persistentDataPath, "actions");

        public static ActionCatalogDef? Load(string catalogName = DefaultCatalogName)
        {
            // Tier 1: external override.
            var overridePath = Path.Combine(OverrideDirectory, catalogName + ".json");
            if (File.Exists(overridePath))
            {
                var result = ActionCatalogLoader.Parse(SafeReadAllText(overridePath));
                if (result.Success && result.Catalog != null)
                {
                    SafetyLog.Info($"[ActionCatalogSource] Catálogo de ações carregado do override externo: {overridePath}");
                    return result.Catalog;
                }

                SafetyLog.Warning(
                    $"[ActionCatalogSource] Override '{overridePath}' inválido, usando o catálogo embarcado. " +
                    $"Motivo: {result.ErrorSummary}");
            }

            // Tier 2: embedded default.
            var textAsset = Resources.Load<TextAsset>($"Actions/{catalogName}");
            if (textAsset == null)
            {
                SafetyLog.Warning(
                    $"[ActionCatalogSource] Catálogo embarcado 'Resources/Actions/{catalogName}' não encontrado.");
                return null;
            }

            var embedded = ActionCatalogLoader.Parse(textAsset.text);
            if (embedded.Success && embedded.Catalog != null)
            {
                return embedded.Catalog;
            }

            SafetyLog.Error(
                $"[ActionCatalogSource] Catálogo embarcado '{catalogName}' é inválido: {embedded.ErrorSummary}");
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
                SafetyLog.Warning($"[ActionCatalogSource] Falha ao ler '{path}': {ex.Message}");
                return string.Empty;
            }
        }
    }
}
