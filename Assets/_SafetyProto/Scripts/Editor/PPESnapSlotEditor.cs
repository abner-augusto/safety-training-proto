using System.Collections.Generic;
using SafetyProto.Runtime.PPE;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Editor
{
    /// <summary>
    /// Inspector helper that previews how each assigned <see cref="PPESnapItem"/> will be posed
    /// when snapped into this slot. The wireframe gizmo runs purely off the existing snap math;
    /// the "Spawn Live Preview" button instantiates the items as editor-only children so you can
    /// inspect materials, child anchors, and collider alignment in context.
    /// </summary>
    [CustomEditor(typeof(PPESnapSlot))]
    public class PPESnapSlotEditor : UnityEditor.Editor
    {
        private const string PreviewRootName = "__PPESnapPreview";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var slot = (PPESnapSlot)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Snap Preview Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Assign PPE item prefabs (or scene instances) in 'Editor Preview Items' above to see a " +
                "wireframe of where they will land when snapped. Use 'Spawn Live Preview' to instantiate " +
                "them as editor-only children for full-fidelity inspection. Live previews are not saved " +
                "with the scene and are skipped at build time (HideFlags.DontSaveInBuild | EditorOnly tag).",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Spawn Live Preview"))
                    SpawnLivePreviews(slot);

                if (GUILayout.Button("Clear Live Preview"))
                    ClearLivePreviews(slot);
            }

            if (GUILayout.Button("Refresh Live Preview"))
            {
                ClearLivePreviews(slot);
                SpawnLivePreviews(slot);
            }
        }

        private static void SpawnLivePreviews(PPESnapSlot slot)
        {
            var items = slot.EditorPreviewItems;
            if (items == null || items.Length == 0)
            {
                Debug.LogWarning($"[PPESnapSlotEditor] {slot.name}: no editor preview items assigned.", slot);
                return;
            }

            ClearLivePreviews(slot);

            var root = new GameObject(PreviewRootName)
            {
                hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor,
                tag = "EditorOnly"
            };
            root.transform.SetParent(slot.transform, false);

            int spawned = 0;
            foreach (var item in items)
            {
                if (item == null) continue;

                PPESnapSlot.ComputeSnapPose(item, slot.transform.position, slot.transform.rotation,
                    out var pos, out var rot);

                GameObject instance;
                if (PrefabUtility.IsPartOfPrefabAsset(item.gameObject))
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(item.gameObject, root.transform.parent);
                else
                    instance = Instantiate(item.gameObject, root.transform.parent);

                instance.name = $"{PreviewRootName}_{item.name}";
                instance.tag = "EditorOnly";
                instance.transform.SetPositionAndRotation(pos, rot);
                instance.transform.SetParent(root.transform, true);

                StripRuntimeBehaviours(instance);
                SetHideFlagsRecursive(instance, HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor);

                spawned++;
            }

            Selection.activeGameObject = root;
            Debug.Log($"[PPESnapSlotEditor] {slot.name}: spawned {spawned} live preview(s).", slot);
        }

        private static void ClearLivePreviews(PPESnapSlot slot)
        {
            var toDelete = new List<GameObject>();
            for (int i = 0; i < slot.transform.childCount; i++)
            {
                var child = slot.transform.GetChild(i);
                if (child.name == PreviewRootName)
                    toDelete.Add(child.gameObject);
            }

            foreach (var go in toDelete)
                DestroyImmediate(go);
        }

        // Removes components that would otherwise try to wire themselves into runtime systems
        // (EventBus subscriptions, grab interactables, etc.) on a preview-only instance.
        private static void StripRuntimeBehaviours(GameObject instance)
        {
            // Order matters: PPESnapItem requires Rigidbody, so remove PPESnapItem first.
            DestroyAllOfType<PPESnapItem>(instance);
            DestroyAllOfType<Rigidbody>(instance);

            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }

        private static void DestroyAllOfType<T>(GameObject go) where T : Component
        {
            foreach (var c in go.GetComponentsInChildren<T>(true))
                DestroyImmediate(c);
        }

        private static void SetHideFlagsRecursive(GameObject go, HideFlags flags)
        {
            go.hideFlags = flags;
            for (int i = 0; i < go.transform.childCount; i++)
                SetHideFlagsRecursive(go.transform.GetChild(i).gameObject, flags);
        }
    }
}
