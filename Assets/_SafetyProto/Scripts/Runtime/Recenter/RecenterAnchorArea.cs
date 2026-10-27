using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Describes the physical play area around a recenter anchor: the calibrated room center
    /// lands on this transform with the room's front along its forward. A participant arriving
    /// away from the center keeps that offset, clamped into the landing ranges so they never
    /// arrive inside scenery or past the edge of a narrow floor. The Scene view shows the area,
    /// arm reach and landing zone, so content placed out of reach is visible while authoring.
    /// </summary>
    public class RecenterAnchorArea : MonoBehaviour
    {
        [Tooltip("Physical play area in meters: x = side to side, y = front to back.")]
        [SerializeField] private Vector2 size = new Vector2(3f, 3f);
        [Tooltip("Typical arm reach added around the area (faint box).")]
        [SerializeField] private float armReach = 0.6f;

        [Header("Landing")]
        [Tooltip("Allowed body offset to the side on arrival, in meters (negative = left).")]
        [SerializeField] private Vector2 lateralRange = new Vector2(-1.5f, 1.5f);
        [Tooltip("Allowed body offset to the front on arrival, in meters (negative = back).")]
        [SerializeField] private Vector2 depthRange = new Vector2(-1.5f, 1.5f);

        [SerializeField] private Color color = new Color(0.1f, 0.9f, 0.4f, 1f);

        /// <summary>Clamps a body offset from the anchor (x = right, y = forward) into the landing zone.</summary>
        public Vector2 ClampLanding(Vector2 offset) => new Vector2(
            Mathf.Clamp(offset.x, lateralRange.x, lateralRange.y),
            Mathf.Clamp(offset.y, depthRange.x, depthRange.y));

        private void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);

            Gizmos.color = color;
            Gizmos.DrawWireCube(new Vector3(0f, 0.01f, 0f), new Vector3(size.x, 0f, size.y));
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0.01f, size.y * 0.5f));

            Gizmos.color = new Color(1f, 0.8f, 0.1f, 1f);
            var landingCenter = new Vector3((lateralRange.x + lateralRange.y) * 0.5f, 0.02f, (depthRange.x + depthRange.y) * 0.5f);
            Gizmos.DrawWireCube(landingCenter, new Vector3(lateralRange.y - lateralRange.x, 0f, depthRange.y - depthRange.x));

            Gizmos.color = new Color(color.r, color.g, color.b, 0.35f);
            Gizmos.DrawWireCube(new Vector3(0f, 1.2f, 0f), new Vector3(size.x + 2f * armReach, 2.4f, size.y + 2f * armReach));

            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
