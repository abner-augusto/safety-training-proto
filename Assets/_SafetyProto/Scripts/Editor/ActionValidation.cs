using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.Actions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SafetyProto.Editor.Actions
{
    public static class ActionValidation
    {
        private const string ActionIdRegex = "^[a-z0-9_\\-]+$";
        private static readonly Regex ActionIdPattern = new Regex(ActionIdRegex, RegexOptions.Compiled);

        [MenuItem("SafetyProto/Validation/Validate Actions & Tasks")]
        public static void ValidateAll()
        {
            var registry = LoadRegistry();
            if (registry == null)
            {
                Debug.LogError("[ActionValidation] ActionRegistry could not be found. Ensure one exists in Resources.");
            }
            else
            {
                ValidateRegistry(registry);
            }

            ValidateSafetyTasks(registry);
            ValidateEmitters(registry);

            Debug.Log("[ActionValidation] Validation completed.");
        }

        private static ActionRegistry LoadRegistry()
        {
            return Resources.Load<ActionRegistry>("ActionRegistry");
        }

        private static void ValidateRegistry(ActionRegistry registry)
        {
            var ids = new HashSet<string>();
            foreach (var action in registry.Actions)
            {
                if (action == null)
                {
                    Debug.LogError($"[ActionValidation] Null ActionType entry in registry '{registry.name}'.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(action.ActionId))
                {
                    Debug.LogError($"[ActionValidation] ActionType '{action.name}' has empty ActionId.");
                    continue;
                }

                var id = action.ActionId.Trim();
                if (!ActionIdPattern.IsMatch(id))
                {
                    Debug.LogWarning($"[ActionValidation] ActionId '{id}' on '{action.name}' does not match regex {ActionIdRegex}.");
                }

                if (!ids.Add(id))
                {
                    Debug.LogError($"[ActionValidation] Duplicate ActionId '{id}' in registry '{registry.name}'.");
                }
            }
        }

        private static void ValidateSafetyTasks(ActionRegistry registry)
        {
            var tasks = AssetDatabase.FindAssets("t:SafetyTask")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Select(path => AssetDatabase.LoadAssetAtPath<SafetyTask>(path))
                .Where(task => task != null);

            foreach (var task in tasks)
            {
                var actionId = task.ResolveExpectedActionId();
                if (string.IsNullOrEmpty(actionId))
                {
                    Debug.LogError($"[ActionValidation] SafetyTask '{task.name}' missing expected action id.");
                }
                else if (!ActionIdPattern.IsMatch(actionId))
                {
                    Debug.LogWarning($"[ActionValidation] SafetyTask '{task.name}' expected action '{actionId}' violates regex {ActionIdRegex}.");
                }
                else if (registry != null && !registry.TryGet(actionId, out _))
                {
                    Debug.LogError($"[ActionValidation] SafetyTask '{task.name}' references unknown action '{actionId}'.");
                }
            }
        }

        private static void ValidateEmitters(ActionRegistry registry)
        {
            ValidateEmittersInPrefabs(registry);
            ValidateEmittersInOpenScenes(registry);
        }

        private static void ValidateEmittersInPrefabs(ActionRegistry registry)
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                foreach (var emitter in prefab.GetComponentsInChildren<ActionEmitter>(true))
                {
                    ValidateEmitterInstance(emitter, path, registry);
                }
            }
        }

        private static void ValidateEmittersInOpenScenes(ActionRegistry registry)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var emitter in root.GetComponentsInChildren<ActionEmitter>(true))
                    {
                        ValidateEmitterInstance(emitter, scene.path, registry);
                    }
                }
            }
        }

        private static void ValidateEmitterInstance(ActionEmitter emitter, string context, ActionRegistry registry)
        {
            if (emitter == null)
            {
                return;
            }

            var actionId = emitter.ConfiguredActionId;
            if (string.IsNullOrEmpty(actionId))
            {
                Debug.LogError($"[ActionValidation] ActionEmitter '{emitter.name}' in '{context}' missing action id.");
                return;
            }

            if (!ActionIdPattern.IsMatch(actionId))
            {
                Debug.LogWarning($"[ActionValidation] ActionEmitter '{emitter.name}' in '{context}' uses action id '{actionId}' that violates regex {ActionIdRegex}.");
            }
            else if (registry != null && !registry.TryGet(actionId, out _))
            {
                Debug.LogWarning($"[ActionValidation] ActionEmitter '{emitter.name}' in '{context}' references unknown action '{actionId}'.");
            }
        }
    }
}
