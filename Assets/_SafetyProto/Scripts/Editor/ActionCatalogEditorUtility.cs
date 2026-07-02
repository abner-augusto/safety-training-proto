#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SafetyProto.Domain.Actions;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Editor
{
    internal static class ActionCatalogEditorUtility
    {
        private const string CatalogPath = "Assets/_SafetyProto/Resources/Actions/actions.json";

        public static IReadOnlyList<string> LoadActionIds()
        {
            var catalog = LoadCatalog();
            if (catalog == null) return System.Array.Empty<string>();

            return catalog.Actions
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.ActionId))
                .Select(a => a.ActionId.Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static HashSet<string> LoadActionIdSet() =>
            new(LoadActionIds(), System.StringComparer.OrdinalIgnoreCase);

        public static ActionCatalogDef? LoadCatalog()
        {
            if (!File.Exists(CatalogPath))
            {
                Debug.LogWarning($"[SafetyProto] Catálogo de ações não encontrado: {CatalogPath}");
                return null;
            }

            var result = ActionCatalogLoader.Parse(File.ReadAllText(CatalogPath));
            if (result.Success && result.Catalog != null) return result.Catalog;

            Debug.LogWarning($"[SafetyProto] Catálogo de ações inválido: {result.ErrorSummary}");
            return null;
        }
    }
}
