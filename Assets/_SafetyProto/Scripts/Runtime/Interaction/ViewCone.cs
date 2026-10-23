using UnityEngine;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// "Is the participant looking at this point?" without a collider: the point lies within
    /// <c>maxDistance</c> of the eye and within <c>maxAngleDegrees</c> of the head's forward vector.
    /// Used for world-space panels that are read, not aimed at, where a raycast against a thin canvas
    /// would be too strict.
    /// </summary>
    public static class ViewCone
    {
        public static bool Contains(Vector3 eye, Vector3 forward, Vector3 point, float maxAngleDegrees, float maxDistance)
        {
            var toPoint = point - eye;
            float distance = toPoint.magnitude;
            if (distance > maxDistance) return false;
            if (distance < 1e-4f) return true;
            if (forward.sqrMagnitude < 1e-8f) return false;
            return Vector3.Angle(forward, toPoint) <= maxAngleDegrees;
        }
    }
}
