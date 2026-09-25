using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Editor
{
    public sealed class DistractorPrimitiveColliderImporter : AssetPostprocessor
    {
        private bool IsColliderModel =>
            Path.GetFileNameWithoutExtension(assetPath).StartsWith("SM_Distractor_", StringComparison.Ordinal) &&
            Path.GetFileNameWithoutExtension(assetPath).EndsWith("_PrimitiveColliders", StringComparison.Ordinal);

        private void OnPreprocessModel()
        {
            if (IsColliderModel)
                ((ModelImporter)assetImporter).addCollider = false;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsColliderModel)
                return;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("SPColBox_", StringComparison.Ordinal) || filter.sharedMesh == null)
                    continue;

                Bounds bounds = filter.sharedMesh.bounds;
                BoxCollider collider = filter.gameObject.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = bounds.size;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null)
                    UnityEngine.Object.DestroyImmediate(renderer);
                UnityEngine.Object.DestroyImmediate(filter);
            }
        }
    }
}
