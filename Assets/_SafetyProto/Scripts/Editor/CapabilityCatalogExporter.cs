#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SafetyProto.Domain.Actions;
using SafetyProto.Domain.Capabilities;
using SafetyProto.Runtime.Actions;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Editor
{
    /// <summary>
    /// Exports a <see cref="CapabilityCatalog"/> describing what this build implements —
    /// registered action ids, available PPE types, and build scenes/phases — to
    /// <c>Tools/AuthoringApp/capability_catalog.json</c>. The desktop authoring app reads
    /// it so a safety specialist can only pick options that actually exist, and validate
    /// scenarios (via <c>ScenarioValidator</c>) before deploying them to the headset.
    /// </summary>
    public static class CapabilityCatalogExporter
    {
        [MenuItem("SafetyProto/Export Capability Catalog")]
        public static void Export()
        {
            var catalog = new CapabilityCatalog
            {
                Version = Application.version,
                ActionIds = CollectActionIds(),
                PpeTypes = Enum.GetNames(typeof(SafetyProto.Core.PPEType))
                    .Where(n => n != "None")
                    .ToList(),
                Phases = CollectScenes(),
            };

            var json = JsonConvert.SerializeObject(catalog, Formatting.Indented);
            var dir = Path.Combine(GetProjectRoot(), "Tools", "AuthoringApp");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "capability_catalog.json");
            File.WriteAllText(path, json);

            Debug.Log($"[CapabilityCatalogExporter] Wrote '{path}' — {catalog.ActionIds.Count} actions, " +
                      $"{catalog.PpeTypes.Count} PPE, {catalog.Phases.Count} scenes.");
        }

        private static List<string> CollectActionIds()
        {
            var textAsset = Resources.Load<TextAsset>($"Actions/{ActionCatalogSource.DefaultCatalogName}");
            if (textAsset == null)
            {
                Debug.LogWarning("[CapabilityCatalogExporter] Resources/Actions/actions not found; action list will be empty.");
                return new List<string>();
            }

            var result = ActionCatalogLoader.Parse(textAsset.text);
            if (!result.Success || result.Catalog == null)
            {
                Debug.LogWarning($"[CapabilityCatalogExporter] Resources/Actions/actions invalid: {result.ErrorSummary}");
                return new List<string>();
            }

            return result.Catalog.Actions
                .Where(a => !string.IsNullOrWhiteSpace(a.ActionId))
                .Select(a => a.ActionId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> CollectScenes()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => Path.GetFileNameWithoutExtension(s.path))
                .ToList();
        }

        private static string GetProjectRoot()
        {
            // Application.dataPath is "<root>/Assets".
            return Directory.GetParent(Application.dataPath)!.FullName;
        }
    }
}
