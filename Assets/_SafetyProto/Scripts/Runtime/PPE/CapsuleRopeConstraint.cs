using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    internal static class CapsuleRopeConstraint
    {
        internal static void GetWorldCapsule(CapsuleCollider capsule, out Vector3 a, out Vector3 b, out float radius)
        {
            var frame = capsule.transform;
            Vector3 scale = frame.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            int direction = capsule.direction;
            Vector3 axis = direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward;
            float axisScale = scale[direction];
            float radialScale = Mathf.Max(scale[(direction + 1) % 3], scale[(direction + 2) % 3]);
            radius = capsule.radius * radialScale;
            float halfLine = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
            Vector3 center = frame.TransformPoint(capsule.center);
            Vector3 offset = frame.TransformDirection(axis) * halfLine;
            a = center - offset;
            b = center + offset;
        }

        internal static void ProjectSegment(ref Vector3 first, ref Vector3 second, bool firstPinned,
            bool secondPinned, Vector3 a, Vector3 b, float radius, Vector3 fallback)
        {
            ClosestPoints(first, second, a, b, out float t, out Vector3 point, out Vector3 axisPoint);
            Vector3 radial = point - axisPoint;
            float distance = radial.magnitude;
            if (distance >= radius || (firstPinned && secondPinned)) return;
            Vector3 normal = distance > 0.000001f ? radial / distance : Vector3.ProjectOnPlane(fallback, b - a).normalized;
            if (normal.sqrMagnitude < 0.001f)
            {
                Vector3 axis = (b - a).normalized;
                normal = axis.sqrMagnitude < 0.001f ? Vector3.right
                    : Vector3.Cross(axis, Mathf.Abs(axis.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
            }
            float firstWeight = firstPinned ? 0f : 1f - t;
            float secondWeight = secondPinned ? 0f : t;
            float denominator = firstWeight * firstWeight + secondWeight * secondWeight;
            // A pinned endpoint inside the body is an invalid authored pose; do not
            // launch its neighbour trying to satisfy an impossible constraint.
            if (denominator < 0.0001f) return;
            Vector3 correction = normal * (radius - distance + 0.0001f) / denominator;
            first += correction * firstWeight;
            second += correction * secondWeight;
        }

        private static void ClosestPoints(Vector3 p, Vector3 q, Vector3 a, Vector3 b,
            out float t, out Vector3 point, out Vector3 axisPoint)
        {
            Vector3 u = q - p;
            Vector3 v = b - a;
            Vector3 w = p - a;
            float uu = Vector3.Dot(u, u);
            float vv = Vector3.Dot(v, v);
            float uv = Vector3.Dot(u, v);
            float uw = Vector3.Dot(u, w);
            float vw = Vector3.Dot(v, w);
            float s;
            if (uu < 1e-10f)
            {
                t = 0f;
                s = vv < 1e-10f ? 0f : Mathf.Clamp01(vw / vv);
            }
            else if (vv < 1e-10f)
            {
                s = 0f;
                t = Mathf.Clamp01(-uw / uu);
            }
            else
            {
                float determinant = uu * vv - uv * uv;
                t = determinant > 1e-10f ? Mathf.Clamp01((uv * vw - uw * vv) / determinant) : 0f;
                s = (uv * t + vw) / vv;
                if (s < 0f) { s = 0f; t = Mathf.Clamp01(-uw / uu); }
                else if (s > 1f) { s = 1f; t = Mathf.Clamp01((uv - uw) / uu); }
            }
            point = p + u * t;
            axisPoint = a + v * s;
        }
    }
}
