#nullable enable
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Domain.Actions;
using SafetyProto.Runtime.Actions;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Editor
{
    /// <summary>
    /// One-shot (re-)bake of the <see cref="ActionRegistry"/> ScriptableObject into the unified
    /// action-catalog JSON consumed at runtime by <see cref="ActionCatalogSource"/> and by the
    /// desktop authoring app. Copies only the <em>logical</em> half of each ActionTypeSO —
    /// presentation stays on the asset. Transitional: lives until the SOs are removed.
    /// </summary>
    public static class ActionCatalogExporter
    {
        private const string ResourcesDir = "Assets/_SafetyProto/Resources/Actions";
        private const string RegistryResource = "ActionRegistry";

        [MenuItem("SafetyProto/Bake Action Catalog to JSON")]
        public static void BakeRegistry()
        {
            var registry = Resources.Load<ActionRegistry>(RegistryResource);
            if (registry == null)
            {
                EditorUtility.DisplayDialog("Bake Action Catalog",
                    $"ActionRegistry não encontrado em Resources/{RegistryResource}.", "OK");
                return;
            }

            var catalog = BuildCatalog(registry);
            var json = JsonConvert.SerializeObject(catalog, Formatting.Indented);

            Directory.CreateDirectory(ResourcesDir);
            var path = Path.Combine(ResourcesDir, ActionCatalogSource.DefaultCatalogName + ".json");
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();

            // Validate the just-written file through the same loader the runtime uses.
            var result = ActionCatalogLoader.Parse(json);
            if (!result.Success || result.Catalog == null)
            {
                Debug.LogError($"[ActionCatalogExporter] Baked '{path}' but it failed validation: {result.ErrorSummary}");
                return;
            }

            Debug.Log($"[ActionCatalogExporter] Baked '{path}' OK — {result.Catalog.Actions.Count} actions.");
        }

        private static ActionCatalogDef BuildCatalog(ActionRegistry registry)
        {
            var catalog = new ActionCatalogDef { Version = "1" };

            foreach (var so in registry.Actions)
            {
                if (so == null || string.IsNullOrWhiteSpace(so.ActionId)) continue;

                catalog.Actions.Add(new ActionDef
                {
                    ActionId = so.ActionId,
                    DisplayName = so.DisplayName,
                    Description = so.Description,
                    Category = so.Category.ToString(),
                    TelemetryName = so.TelemetryName,
                    Tags = new List<string>(so.Tags),
                    RegulatoryRefs = new List<string>(so.RegulatoryRefs),
                    IsSafetyCritical = so.IsSafetyCritical,
                    IsHiddenInUI = so.IsHiddenInUI,
                    CooldownSeconds = so.CooldownSeconds,
                    ExpectedDurationSeconds = so.ExpectedDurationSeconds,
                    BaseScore = so.BaseScore,
                    Severity = so.Severity,
                });
            }

            return catalog;
        }
    }
}
