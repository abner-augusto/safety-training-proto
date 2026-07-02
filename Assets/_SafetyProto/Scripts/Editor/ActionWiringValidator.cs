#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Editor
{
    public static class ActionWiringValidator
    {
        private static readonly HashSet<string> ActionFieldNames = new(System.StringComparer.Ordinal)
        {
            "actionId",
            "connectActionId",
        };

        [MenuItem("SafetyProto/Validate Action Wiring")]
        public static void ValidateActionWiring()
        {
            var knownIds = ActionCatalogEditorUtility.LoadActionIdSet();
            var problems = new List<string>();
            var seenIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ScanObject(behaviour, GetScenePath(behaviour), knownIds, seenIds, problems);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_SafetyProto" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    ScanObject(behaviour, path + "/" + GetTransformPath(behaviour.transform), knownIds, seenIds, problems);
                }
            }

            var unused = knownIds.Except(seenIds, System.StringComparer.OrdinalIgnoreCase).OrderBy(id => id).ToList();

            if (problems.Count == 0)
            {
                Debug.Log($"[SafetyProto] Validação de action wiring concluída: nenhum actionId inválido encontrado. " +
                          $"{seenIds.Count} actionId(s) usados, {unused.Count} sem emissor na cena/prefabs.");
            }
            else
            {
                Debug.LogWarning("[SafetyProto] Problemas de action wiring:\n- " + string.Join("\n- ", problems));
            }

            if (unused.Count > 0)
            {
                Debug.Log($"[SafetyProto] actionIds sem emissor encontrado na cena/prefabs: {string.Join(", ", unused)}");
            }
        }

        private static void ScanObject(
            MonoBehaviour behaviour,
            string objectPath,
            HashSet<string> knownIds,
            HashSet<string> seenIds,
            List<string> problems)
        {
            if (behaviour == null) return;

            var serialized = new SerializedObject(behaviour);
            var property = serialized.GetIterator();
            var enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyType != SerializedPropertyType.String) continue;
                if (!ActionFieldNames.Contains(property.name)) continue;

                var id = property.stringValue?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id)) continue;

                seenIds.Add(id);
                if (knownIds.Count > 0 && !knownIds.Contains(id))
                {
                    problems.Add($"{objectPath} ({behaviour.GetType().Name}.{property.propertyPath}) usa actionId inexistente '{id}'.");
                }
            }
        }

        private static string GetScenePath(Component component) =>
            component.gameObject.scene.name + "/" + GetTransformPath(component.transform);

        private static string GetTransformPath(Transform transform)
        {
            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }
    }
}
