using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// The scene's single source of head-gaze. Casts one ray per frame from the HMD centre eye and
    /// notifies the <see cref="IGazeTarget"/> it lands on.
    ///
    /// Quest 3 has no eye tracking, so this is head gaze: the forward vector of CenterEyeAnchor.
    /// That is not a downgrade here — the participant has to point their head at the defect, which
    /// is the inspection posture the scenario is trying to teach.
    ///
    /// One raycast total, regardless of how many targets the scene grows. The cast is restricted to
    /// the GazeTarget layer plus a small occluder mask, and ignores triggers, so unrelated trigger
    /// volumes (PPE slots, invisible walls) can never masquerade as occlusion.
    /// </summary>
    public class HeadGazeSource : MonoBehaviour
    {
        [Header("Ray origin")]
        [Tooltip("HMD centre eye. Leave empty to use the MainCamera-tagged camera (CenterEyeAnchor).")]
        [SerializeField] private Transform _gazeOrigin;

        [Header("Rules")]
        [Tooltip("Layer holding gaze target colliders. Set to 'GazeTarget'.")]
        [SerializeField] private LayerMask _gazeTargetMask = 1 << 17;

        [Tooltip("Solid geometry that blocks gaze. Default + Object + ScaffoldInteractive. " +
                 "Deliberately excludes InvisibleWall, which is boundary geometry, not scenery.")]
        [SerializeField] private LayerMask _occluderMask = (1 << 0) | (1 << 6) | (1 << 14);

        [Tooltip("Maximum gaze distance in metres. Keeping this short means the tear can only be " +
                 "reported from the platform, while exposed to the fall risk, as the task intends.")]
        [SerializeField, Range(0.5f, 20f)] private float _maxDistance = 3.5f;

        [Header("Debug")]
        [SerializeField] private bool _drawDebugRay;

        private Transform _resolvedOrigin;
        private bool _originMissingLogged;

        public float MaxDistance => _maxDistance;

        private void Awake() => ResolveOrigin();

        private void Update()
        {
            if (_resolvedOrigin == null && !ResolveOrigin()) return;

            var ray = new Ray(_resolvedOrigin.position, _resolvedOrigin.forward);

            if (_drawDebugRay)
                Debug.DrawRay(ray.origin, ray.direction * _maxDistance, Color.cyan);

            ResolveTarget(ray)?.OnGazed(Time.deltaTime);
        }

        /// <summary>
        /// Returns the gaze target the ray lands on, or null. Public so the layer, distance and
        /// occlusion rules can be tested without a headset.
        /// </summary>
        public IGazeTarget ResolveTarget(Ray ray)
        {
            int mask = _gazeTargetMask.value | _occluderMask.value;

            if (!Physics.Raycast(ray, out RaycastHit hit, _maxDistance, mask, QueryTriggerInteraction.Ignore))
                return null;

            // The first thing hit must BE the target. If a scaffold tube is nearer, the participant
            // is looking through the structure and that does not count as having inspected anything.
            if ((_gazeTargetMask.value & (1 << hit.collider.gameObject.layer)) == 0)
                return null;

            return hit.collider.GetComponentInParent<IGazeTarget>();
        }

        private bool ResolveOrigin()
        {
            if (_gazeOrigin != null)
            {
                _resolvedOrigin = _gazeOrigin;
                return true;
            }

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                _resolvedOrigin = mainCamera.transform;
                return true;
            }

            if (!_originMissingLogged)
            {
                _originMissingLogged = true;
                SafetyLog.Error("[HeadGazeSource] No gaze origin: assign CenterEyeAnchor or tag a camera as MainCamera. Gaze detection is off.", this);
            }

            return false;
        }
    }
}
