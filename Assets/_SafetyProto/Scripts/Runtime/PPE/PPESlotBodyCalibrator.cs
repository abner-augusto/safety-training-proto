using System.Collections;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    /// <summary>
    /// Repositions this slot's local Y at startup so it lands at ankle height
    /// based on the player's actual standing height, replacing any hardcoded
    /// offset from CenterEyeAnchor that breaks for non-average-height players.
    ///
    /// Primary strategy: raycast downward from eye position to find the scene
    /// floor, then place the slot <see cref="ankleOffset"/> above the hit point.
    /// Fallback: estimate ankle height from eye height using standard human
    /// proportions (ankle ≈ eye height × 0.044).
    ///
    /// X and Z local offsets are preserved — only Y is corrected.
    /// Call <see cref="Recalibrate"/> at any point to rerun (e.g. after teleport).
    /// </summary>
    [AddComponentMenu("SafetyProto/PPE/PPE Slot Body Calibrator")]
    public class PPESlotBodyCalibrator : MonoBehaviour
    {
        [Header("Ankle Height")]
        [Tooltip("Height above the floor where the snap zone center sits. Ankle bone ≈ 0.12 m.")]
        [SerializeField] private float ankleOffset = 0.12f;

        [Header("Floor Raycast")]
        [Tooltip("Maximum distance to cast downward when searching for the floor.")]
        [SerializeField] private float raycastMaxDistance = 3f;
        [Tooltip("Layers considered as floor. Exclude the player, PPE, and trigger-only layers.")]
        [SerializeField] private LayerMask floorMask = ~0;

        [Header("Tracking Wait (Quest)")]
        [Tooltip("Skip calibration until the HMD reports an eye height above this many metres. Prevents a Start-time raycast from Y≈0 when Quest tracking hasn't initialised yet.")]
        [SerializeField] private float minimumEyeYToCalibrate = 0.5f;
        [Tooltip("Hard cap (seconds) on how long to wait for valid tracking before calibrating anyway.")]
        [SerializeField] private float trackingWaitTimeout = 2f;

        [Header("Fallback")]
        [Tooltip("Used when the floor raycast misses. Ankle world Y = eye height × this ratio.\n" +
                 "Standard anatomy: ankle ≈ 4.4% of eye height above floor (assumes floor at world Y = 0).")]
        [SerializeField] private float proportionalRatio = 0.044f;

#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField] private bool drawDebugRay = true;
#endif

        private Transform _eyeAnchor;

        private IEnumerator Start()
        {
            _eyeAnchor = transform.parent;

            if (_eyeAnchor == null)
            {
                SafetyLog.Warning($"PPESlotBodyCalibrator em {name}: sem parent transform — calibração ignorada.", this);
                yield break;
            }

            // On Quest, the HMD pose isn't applied to CenterEyeAnchor until a few frames after Start().
            // Calibrating immediately would cast from Y≈0 and miss any floor above origin.
            float waited = 0f;
            while (_eyeAnchor.position.y < minimumEyeYToCalibrate && waited < trackingWaitTimeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Calibrate();
        }

        /// <summary>
        /// Recomputes and applies the slot's Y position. Safe to call at runtime
        /// (e.g. after a teleport or scene load).
        /// </summary>
        public void Recalibrate()
        {
            if (_eyeAnchor == null)
            {
                _eyeAnchor = transform.parent;
                if (_eyeAnchor == null) return;
            }

            Calibrate();
        }

        private void Calibrate()
        {
            float eyeWorldY = _eyeAnchor.position.y;
            var rayOrigin = new Vector3(_eyeAnchor.position.x, eyeWorldY, _eyeAnchor.position.z);

            float targetWorldY;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit,
                    raycastMaxDistance, floorMask, QueryTriggerInteraction.Ignore))
            {
                targetWorldY = hit.point.y + ankleOffset;
                SafetyLog.Info(
                    $"PPESlotBodyCalibrator [{name}]: chão detectado em Y={hit.point.y:F3} ({hit.collider.name})" +
                    $" — slot posicionado em Y={targetWorldY:F3}.", this);

#if UNITY_EDITOR
                if (drawDebugRay)
                    Debug.DrawLine(rayOrigin, hit.point, Color.green, 5f);
#endif
            }
            else
            {
                // Proportional fallback — assumes scene floor at world Y = 0.
                targetWorldY = eyeWorldY * proportionalRatio;
                SafetyLog.Warning(
                    $"PPESlotBodyCalibrator [{name}]: raycast não encontrou chão (máx {raycastMaxDistance:F1} m)" +
                    $" — usando proporção anatômica, slot em Y={targetWorldY:F3}.", this);

#if UNITY_EDITOR
                if (drawDebugRay)
                    Debug.DrawRay(rayOrigin, Vector3.down * raycastMaxDistance, Color.yellow, 5f);
#endif
            }

            // Preserve X and Z; only correct Y relative to the eye anchor.
            var local = transform.localPosition;
            local.y = targetWorldY - eyeWorldY;
            transform.localPosition = local;
        }
    }
}
