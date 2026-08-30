using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Lazy-follow ("tag-along") anchor for a head-locked-feeling menu that never actually
    /// head-locks. All the maths lives in <see cref="MenuFollowSolver"/>; this component only
    /// resolves the HMD transform, feeds the solver each <see cref="LateUpdate"/>, and applies the
    /// result. See the solver for the comfort-cone / dwell / hysteresis model.
    /// </summary>
    public class MenuFollowHmd : MonoBehaviour
    {
        [Header("HMD Source")]
        [Tooltip("The HMD camera transform to follow. Auto-assigned from Camera.main if left empty.")]
        [SerializeField]
        private Transform hmdTransform;

        [Header("Placement")]
        [Tooltip("How far in front of the HMD the menu floats, in metres.")]
        public float followDistance = 0.5f;
        [Tooltip("Offset applied in HMD-local space (free mode). In yaw-only mode only Y is used, as a height delta.")]
        public Vector3 menuOffset = Vector3.zero;

        [Header("Comfort Zone")]
        [Tooltip("Metres of head translation drift tolerated before the follow engages.")]
        public float positionDeadzone = 0.15f;
        [Tooltip("Half-angle (degrees) of the horizontal comfort cone. The menu holds still until head-forward is more than this off it.")]
        public float yawDeadzoneDegrees = 12f;
        [Tooltip("Half-angle (degrees) of the vertical comfort cone. Ignored when Yaw Only Follow is on.")]
        public float pitchDeadzoneDegrees = 18f;

        [Header("Dwell")]
        [Tooltip("Seconds the head must stay outside the comfort zone before the follow engages. A quick glance that returns within this window never moves the menu. 0 = immediate.")]
        [Min(0f)]
        public float dwellBeforeFollow = 0.35f;

        [Header("Smoothing")]
        [Tooltip("Vector3.SmoothDamp time for position catch-up. Higher = lazier.")]
        [Min(0.01f)]
        public float positionSmoothTime = 0.25f;
        [Tooltip("SmoothDampAngle time for the facing yaw. Higher = lazier.")]
        [Min(0.01f)]
        public float rotationSmoothTime = 0.15f;

        [Header("Behaviour")]
        [Tooltip("Ride a horizontal ring at a fixed world height and only orbit the user in yaw. Head pitch and vertical bob never move the menu.")]
        public bool yawOnlyFollow = false;
        [Tooltip("Snap straight to the ideal pose when this component is enabled, instead of easing in from wherever it was.")]
        public bool snapOnEnable = true;
        [Tooltip("Once engaged, the follow disengages when the head returns within this fraction of the comfort cone. Hysteresis against boundary chatter.")]
        [Range(0f, 1f)]
        public float reengageFraction = 0.25f;

        private readonly MenuFollowSolver _solver = new MenuFollowSolver();

        private MenuFollowConfig Config => new MenuFollowConfig
        {
            FollowDistance = followDistance,
            MenuOffset = menuOffset,
            PositionDeadzone = positionDeadzone,
            YawDeadzoneDegrees = yawDeadzoneDegrees,
            PitchDeadzoneDegrees = pitchDeadzoneDegrees,
            DwellBeforeFollow = dwellBeforeFollow,
            PositionSmoothTime = positionSmoothTime,
            RotationSmoothTime = rotationSmoothTime,
            ReengageFraction = reengageFraction,
            YawOnly = yawOnlyFollow,
        };

        /// <summary>True while the menu is actively catching up to the head.</summary>
        public bool IsFollowing => _solver.IsFollowing;

        private void Start()
        {
            ResolveHmd();
        }

        private void OnEnable()
        {
            if (!ResolveHmd()) return;

            _solver.Reset();
            _solver.SnapHeight(hmdTransform.position.y);
            if (snapOnEnable)
                ApplyPose(MenuFollowSolver.ComputeIdealPose(
                    hmdTransform.position, hmdTransform.rotation, Config));
        }

        private void LateUpdate()
        {
            if (hmdTransform == null) return;

            MenuPose next = _solver.Tick(
                new MenuPose(transform.position, transform.rotation),
                hmdTransform.position, hmdTransform.rotation, Config, Time.deltaTime);
            ApplyPose(next);
        }

        /// <summary>
        /// Re-pins the yaw-only height, clears the follow state and snaps the menu squarely in
        /// front of the head. Handy for a "bring the menu to me" control.
        /// </summary>
        public void Recenter()
        {
            if (!ResolveHmd()) return;

            _solver.Reset();
            _solver.SnapHeight(hmdTransform.position.y);
            ApplyPose(MenuFollowSolver.ComputeIdealPose(
                hmdTransform.position, hmdTransform.rotation, Config));
        }

        private void ApplyPose(MenuPose pose)
        {
            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
        }

        private bool ResolveHmd()
        {
            if (hmdTransform != null) return true;

            if (Camera.main != null)
            {
                hmdTransform = Camera.main.transform;
                return true;
            }

            SafetyLog.Error("MenuFollowHmd: Please assign the HMD Transform (Camera) in the Inspector!", this);
            return false;
        }
    }
}
