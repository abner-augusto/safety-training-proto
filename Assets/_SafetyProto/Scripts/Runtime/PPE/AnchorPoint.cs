using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    /// <summary>
    /// Tag component placed on anchor point GameObjects in the scene.
    /// The <see cref="RetractableLanyardController"/> searches for nearby AnchorPoints
    /// when the player releases the lanyard tip.
    ///
    /// Inspector setup:
    ///   - Place on the olhal de ancoragem (correct, Type A1 per ABNT NBR 16325).
    ///   - Also place on the scaffold montante (incorrect) with <see cref="isCorrectAnchor"/> = false.
    ///   - Add a trigger Collider if you want visual proximity feedback (optional).
    /// </summary>
    public class AnchorPoint : MonoBehaviour
    {
        [Tooltip("True for olhal de ancoragem (ponto de ancoragem fixo, NR-35 compliant). " +
                 "False for incorrect anchor points like scaffold montantes.")]
        public bool isCorrectAnchor = true;

        [Tooltip("Transform where the lanyard tip should attach. " +
                 "If null, uses this GameObject's transform.")]
        public Transform attachPoint;

        /// <summary>
        /// World-space position where the lanyard end should lock.
        /// </summary>
        public Vector3 AttachPosition =>
            attachPoint != null ? attachPoint.position : transform.position;

        /// <summary>
        /// World-space transform for the lanyard end to follow.
        /// </summary>
        public Transform AttachTransform =>
            attachPoint != null ? attachPoint : transform;

        /// <summary>
        /// World-space rotation the carabiner should adopt when locked.
        /// Authored by orienting the <see cref="attachPoint"/> empty.
        /// </summary>
        public Quaternion AttachRotation =>
            attachPoint != null ? attachPoint.rotation : transform.rotation;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Visualize the snap reference pose so the attachPoint empty can be
            // authored to the exact spot/orientation where the carabiner should sit.
            Transform t = AttachTransform;
            Gizmos.color = isCorrectAnchor ? Color.green : new Color(1f, 0.4f, 0f);
            Gizmos.DrawWireSphere(t.position, 0.02f);

            // Local axes (forward = blue, up = green) to make orientation readable.
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(t.position, t.position + t.forward * 0.05f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(t.position, t.position + t.up * 0.05f);
        }
#endif
    }
}