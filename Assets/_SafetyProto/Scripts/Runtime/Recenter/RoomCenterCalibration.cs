using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>Who recorded the current room-center calibration.</summary>
    public enum RoomCenterSource
    {
        /// <summary>No calibration: recenters fall back to the live head pose.</summary>
        None,
        /// <summary>Captured automatically when a session starts; replaced at every session start.</summary>
        SessionStart,
        /// <summary>Captured on the evaluator's command; survives session restarts.</summary>
        Operator,
    }

    /// <summary>
    /// The physical room center (the floor mark the participant stands on) and the room's
    /// forward direction, stored in the rig's local space — the headset's tracking space.
    /// Moving or rotating the rig (recenters, artificial locomotion) does not change where this
    /// point is in the real room, so every recenter can put the same physical spot over each
    /// phase's anchor no matter where the participant happens to stand at that moment.
    ///
    /// A system recenter (long press on the Meta button) moves the tracking origin itself and
    /// silently invalidates the stored pose; the owner must <see cref="Clear"/> it then.
    /// </summary>
    public sealed class RoomCenterCalibration
    {
        // Eye-to-neck offset in head space (the Oculus neck model). The point under the neck is
        // what stands over the floor mark; the eyes swing ~10 cm forward when the head pitches.
        private static readonly Vector3 EyeToNeck = new Vector3(0f, -0.075f, -0.0805f);

        private Vector3 _localPosition;
        private float _localYaw;

        public RoomCenterSource Source { get; private set; } = RoomCenterSource.None;
        public bool HasValue => Source != RoomCenterSource.None;

        /// <summary>World point under the neck for a head pose; Y is left at the neck height.</summary>
        public static Vector3 BodyAxis(Vector3 eyePosition, Quaternion headRotation)
        {
            float yaw = PlayerRecenter.HeadingYaw(headRotation);
            return eyePosition + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, EyeToNeck.y, EyeToNeck.z);
        }

        /// <summary>
        /// The point to place over an anchor (instead of the room center itself) so the body,
        /// standing at <paramref name="body"/>, arrives at <paramref name="clampLanding"/>'s
        /// version of its offset from the center. Offsets are in the room frame: x = right,
        /// y = forward. Any clamped amount shifts the room relative to the scene by the same
        /// amount, for that teleport only.
        /// </summary>
        public static Vector3 LandingReference(
            Vector3 center, float roomYaw, Vector3 body, System.Func<Vector2, Vector2> clampLanding)
        {
            var room = Quaternion.Euler(0f, roomYaw, 0f);
            Vector3 local = Quaternion.Inverse(room) * (body - center);
            var offset = new Vector2(local.x, local.z);
            Vector2 landed = clampLanding(offset);
            return center + room * new Vector3(offset.x - landed.x, 0f, offset.y - landed.y);
        }

        /// <summary>Records the head's current body axis and heading as the room center.</summary>
        public void Capture(Transform rig, Vector3 headPosition, Quaternion headRotation, RoomCenterSource source)
        {
            if (rig == null || source == RoomCenterSource.None) return;

            Vector3 axis = BodyAxis(headPosition, headRotation);
            _localPosition = rig.InverseTransformPoint(axis);
            _localYaw = Mathf.DeltaAngle(rig.eulerAngles.y, PlayerRecenter.HeadingYaw(headRotation));
            Source = source;
        }

        /// <summary>Where the calibrated room center and forward currently sit in world space.</summary>
        public bool TryGetWorld(Transform rig, out Vector3 position, out float yaw)
        {
            if (!HasValue || rig == null)
            {
                position = default;
                yaw = 0f;
                return false;
            }

            position = rig.TransformPoint(_localPosition);
            yaw = rig.eulerAngles.y + _localYaw;
            return true;
        }

        public void Clear()
        {
            Source = RoomCenterSource.None;
        }
    }
}
