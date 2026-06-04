using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Deterministic room-scale recenter helper.
    ///
    /// The OVRCameraRig root is the room-scale ORIGIN, not the player. The head/camera
    /// sits at a variable XZ offset from the rig depending on where the player physically
    /// stands in their playspace, so moving the rig to a target (<c>rig.position = target</c>)
    /// lands the player off-center by that offset. This helper rotates and translates the rig
    /// so the player's HEAD ends up over the target point at the requested yaw, cancelling the
    /// room-scale offset. Independent of locomotor state — fits a blackout-and-move flow.
    ///
    /// Assumes the rig origin sits at the playspace floor (standard OVRCameraRig): the rig Y is
    /// set to the target's Y so the feet land on the deck and the head rises by the player's real
    /// height.
    /// </summary>
    public static class PlayerRecenter
    {
        /// <summary>
        /// Recenter <paramref name="rig"/> so <paramref name="head"/> lands over
        /// <paramref name="targetPos"/> (XZ), feet at <paramref name="targetPos"/>.y, facing
        /// <paramref name="targetYaw"/> degrees.
        /// </summary>
        public static void Recenter(Transform rig, Transform head, Vector3 targetPos, float targetYaw)
        {
            if (rig == null || head == null) return;

            // 1. Align yaw by rotating the rig about the head's vertical axis (head stays put,
            //    only reorients) so a target yaw is matched without sliding the player.
            float yawDelta = Mathf.DeltaAngle(head.eulerAngles.y, targetYaw);
            rig.RotateAround(head.position, Vector3.up, yawDelta);

            // 2. Translate so the head lands over targetPos (XZ); rig floor at target height (Y).
            //    headOffset is recomputed AFTER the rotation so it reflects the new head XZ.
            Vector3 headOffset = head.position - rig.position;
            rig.position = new Vector3(
                targetPos.x - headOffset.x,
                targetPos.y,
                targetPos.z - headOffset.z);

            // Push the move into the physics scene this frame so any ground probe / first Move
            // after the recenter sees the final position.
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Convenience overload: recenter onto a target Transform (uses its position and Y yaw).
        /// </summary>
        public static void Recenter(Transform rig, Transform head, Transform target)
        {
            if (target == null) return;
            Recenter(rig, head, target.position, target.rotation.eulerAngles.y);
        }

        /// <summary>
        /// Resolves the CenterEyeAnchor (head) transform under a rig by name. Returns null if
        /// not found.
        /// </summary>
        public static Transform ResolveHead(Transform rig)
        {
            if (rig == null) return null;
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
                if (t.name == "CenterEyeAnchor")
                    return t;
            return null;
        }
    }
}
