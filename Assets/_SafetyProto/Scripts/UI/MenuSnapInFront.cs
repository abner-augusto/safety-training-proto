using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Snaps this transform to a fixed, comfortable spot in front of the player every time it is
    /// enabled, then leaves it world-locked until it is disabled again. For menus that should
    /// appear where the player is looking and then stay put (Meta's guidance for menu / status
    /// UI), as opposed to <see cref="MenuFollowHmd"/> which lazily tracks the head.
    ///
    /// Placement reuses <see cref="MenuFollowSolver.ComputeIdealPose"/>: flattened head yaw (so
    /// looking up or down as the menu opens does not throw it to the ceiling or floor), world
    /// height plus <see cref="heightOffset"/>, upright, facing the user.
    /// </summary>
    public class MenuSnapInFront : MonoBehaviour
    {
        [Header("HMD Source")]
        [Tooltip("HMD camera transform. Auto-assigned from Camera.main if left empty.")]
        [SerializeField]
        private Transform hmdTransform;

        [Header("Placement")]
        [Tooltip("Distance in front of the player the menu appears, in metres.")]
        public float distance = 0.6f;
        [Tooltip("Vertical offset from eye height, in metres. Negative sits the menu just below the line of sight, per Meta's comfort guidance.")]
        public float heightOffset = -0.1f;

        private void OnEnable()
        {
            SnapNow();
        }

        /// <summary>Re-places the menu in front of the player right now.</summary>
        public void SnapNow()
        {
            if (!ResolveHmd())
                return;

            var config = new MenuFollowConfig
            {
                FollowDistance = distance,
                MenuOffset = new Vector3(0f, heightOffset, 0f),
                YawOnly = true,
            };

            MenuPose pose = MenuFollowSolver.ComputeIdealPose(
                hmdTransform.position, hmdTransform.rotation, config);
            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
        }

        private bool ResolveHmd()
        {
            if (hmdTransform != null)
                return true;

            if (Camera.main != null)
            {
                hmdTransform = Camera.main.transform;
                return true;
            }

            SafetyLog.Error("MenuSnapInFront: Please assign the HMD Transform (Camera) in the Inspector!", this);
            return false;
        }
    }
}
