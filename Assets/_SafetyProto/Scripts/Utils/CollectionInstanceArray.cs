using System.Collections.Generic;
using UnityEngine;

namespace SafetyProto.Utils
{
    /// <summary>
    /// Attach this component to any GameObject to turn it into an array root.
    /// The Editor companion (CollectionInstanceArrayEditor) rebuilds child
    /// instances automatically whenever a value changes in the Inspector.
    /// </summary>
    [ExecuteAlways]
    public class CollectionInstanceArray : MonoBehaviour
    {
        [Tooltip("Prefab or source GameObject to instantiate.")]
        public GameObject sourcePrefab;

        [Tooltip("Number of instances (including the first one at offset 0).")]
        [Range(1, 50)]
        public int count = 3;

        [Tooltip("Axis along which instances are distributed.")]
        public ArrayAxis axis = ArrayAxis.X;

        [Tooltip("Distance between consecutive instances.")]
        public float offset = 2f;

        [Tooltip("When enabled, 'Offset' is multiplied by the prefab's bounds size on the chosen axis.")]
        public bool relativeOffset = false;

        [HideInInspector]
        public List<GameObject> instances = new();

        public Vector3 GetAxisVector()
        {
            return axis switch
            {
                ArrayAxis.X => Vector3.right,
                ArrayAxis.Y => Vector3.up,
                ArrayAxis.Z => Vector3.forward,
                _           => Vector3.right,
            };
        }

        /// <summary>
        /// Returns the effective step distance, accounting for relative offset.
        /// </summary>
        public float GetEffectiveOffset()
        {
            if (!relativeOffset || sourcePrefab == null)
                return offset;

            // Sample bounds from the prefab's renderers
            var renderers = sourcePrefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return offset;

            Bounds combined = renderers[0].bounds;
            foreach (var r in renderers)
                combined.Encapsulate(r.bounds);

            Vector3 axisVec = GetAxisVector();
            float size = Mathf.Abs(Vector3.Dot(combined.size, axisVec));
            return size > 0f ? size * offset : offset;
        }
    }

    public enum ArrayAxis { X, Y, Z }
}
