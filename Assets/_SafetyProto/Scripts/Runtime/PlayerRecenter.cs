using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Result of a recenter solve: where the rig must be moved/rotated to.
    /// </summary>
    public readonly struct RecenterSolution
    {
        /// <summary>Final world position the rig must be assigned.</summary>
        public readonly Vector3 RigPosition;
        /// <summary>Degrees to rotate the rig about world up, pivoting on the head's position.</summary>
        public readonly float RigYawDelta;

        public RecenterSolution(Vector3 rigPosition, float rigYawDelta)
        {
            RigPosition = rigPosition;
            RigYawDelta = rigYawDelta;
        }
    }

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
        /// Pure solve — no Transform, no side effects. Given the rig origin, the head pose and
        /// the target, returns the rig placement that puts the HEAD over targetPos (XZ) at
        /// targetYaw, with the rig floor at targetPos.y.
        ///
        /// Reproduces the original imperative behavior exactly: the rig position is rotated
        /// about the head's position (the head does not move, since it sits exactly at the
        /// rotation pivot), and the room-scale offset is recomputed AFTER that rotation — it is
        /// the rotated rig position that determines how far off-target the head still is before
        /// the final translation cancels it out.
        /// </summary>
        public static RecenterSolution Solve(
            Vector3 rigPosition, Vector3 headPosition, float headYaw, Vector3 targetPos, float targetYaw)
        {
            // 1. Align yaw by rotating the rig about the head's vertical axis (head stays put,
            //    only reorients) so a target yaw is matched without sliding the player.
            float yawDelta = Mathf.DeltaAngle(headYaw, targetYaw);

            // Analytic form of Transform.RotateAround(headPosition, Vector3.up, yawDelta)
            // applied to rigPosition: rotate the rig's offset from the head, then re-add it.
            Vector3 relative = rigPosition - headPosition;
            Vector3 rotatedRelative = Quaternion.AngleAxis(yawDelta, Vector3.up) * relative;
            Vector3 rigPositionAfterRotation = headPosition + rotatedRelative;

            // 2. Translate so the head lands over targetPos (XZ); rig floor at target height (Y).
            //    headOffset is recomputed AFTER the rotation so it reflects the new head XZ —
            //    the head itself does not move (rotation pivot == head's own position), but the
            //    rig does, so the offset between them changes.
            Vector3 headOffset = headPosition - rigPositionAfterRotation;

            Vector3 finalRigPosition = new Vector3(
                targetPos.x - headOffset.x,
                targetPos.y,
                targetPos.z - headOffset.z);

            return new RecenterSolution(finalRigPosition, yawDelta);
        }

        /// <summary>
        /// Recenter <paramref name="rig"/> so <paramref name="head"/> lands over
        /// <paramref name="targetPos"/> (XZ), feet at <paramref name="targetPos"/>.y, facing
        /// <paramref name="targetYaw"/> degrees.
        /// </summary>
        public static void Recenter(Transform rig, Transform head, Vector3 targetPos, float targetYaw)
        {
            if (rig == null || head == null) return;

            var solution = Solve(rig.position, head.position, head.eulerAngles.y, targetPos, targetYaw);

            rig.RotateAround(head.position, Vector3.up, solution.RigYawDelta);
            rig.position = solution.RigPosition;

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
