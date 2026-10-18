using UnityEngine;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// Computes the root object's world pose required to align a local child anchor with a target socket.
    /// Consolidates socket alignment math across scaffold pieces, PPE snap items, and snap slots.
    /// </summary>
    public static class SocketPoseMath
    {
        /// <summary>
        /// Computes the target root position and rotation to place <paramref name="root"/> such that
        /// <paramref name="localAnchor"/> coincides with <paramref name="socketPosition"/> and
        /// <paramref name="socketRotation"/>.
        /// </summary>
        public static void ComputeRootPose(
            Transform root,
            Transform localAnchor,
            Vector3 socketPosition,
            Quaternion socketRotation,
            out Vector3 targetRootPosition,
            out Quaternion targetRootRotation)
        {
            ComputeRootPose(root, localAnchor, socketPosition, socketRotation, false, out targetRootPosition, out targetRootRotation);
        }

        /// <summary>
        /// Computes the target root position and rotation to place <paramref name="root"/> such that
        /// <paramref name="localAnchor"/> coincides with <paramref name="socketPosition"/> and
        /// <paramref name="socketRotation"/>, optionally choosing a 180-degree yaw flip if it aligns closer.
        /// </summary>
        public static void ComputeRootPose(
            Transform root,
            Transform localAnchor,
            Vector3 socketPosition,
            Quaternion socketRotation,
            bool allow180YawFlip,
            out Vector3 targetRootPosition,
            out Quaternion targetRootRotation)
        {
            if (root == null || localAnchor == null)
            {
                targetRootPosition = socketPosition;
                targetRootRotation = socketRotation;
                return;
            }

            Quaternion targetRotation = socketRotation;
            if (allow180YawFlip)
            {
                Quaternion flipped = socketRotation * Quaternion.Euler(0f, 180f, 0f);
                if (Quaternion.Angle(localAnchor.rotation, flipped) <
                    Quaternion.Angle(localAnchor.rotation, socketRotation))
                {
                    targetRotation = flipped;
                }
            }

            Vector3 anchorLocalPos = root.InverseTransformPoint(localAnchor.position);
            Quaternion anchorLocalRot = Quaternion.Inverse(root.rotation) * localAnchor.rotation;

            ComputeRootPoseFromLocalOffset(
                anchorLocalPos,
                anchorLocalRot,
                socketPosition,
                targetRotation,
                out targetRootPosition,
                out targetRootRotation);
        }

        /// <summary>
        /// Computes the target root position and rotation from precomputed local anchor offsets.
        /// </summary>
        public static void ComputeRootPoseFromLocalOffset(
            Vector3 localAnchorPos,
            Quaternion localAnchorRot,
            Vector3 socketPosition,
            Quaternion socketRotation,
            out Vector3 targetRootPosition,
            out Quaternion targetRootRotation)
        {
            targetRootRotation = socketRotation * Quaternion.Inverse(localAnchorRot);
            targetRootPosition = socketPosition - (targetRootRotation * localAnchorPos);
        }
    }
}
