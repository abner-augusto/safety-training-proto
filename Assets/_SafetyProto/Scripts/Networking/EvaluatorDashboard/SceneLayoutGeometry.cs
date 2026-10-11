using SafetyProto.Domain.Dashboard;
using UnityEngine;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>
    /// Converts a component's local bounds into the world-space floor and marker DTOs the
    /// dashboard draws. Pure over (matrix, bounds) so it is testable without a scene.
    /// </summary>
    public static class SceneLayoutGeometry
    {
        /// <summary>Local-space bounds of <paramref name="source"/>: Renderer.localBounds, a
        /// BoxCollider's box, a MeshCollider's mesh bounds, or a zero-size point for any other
        /// component. False only for a missing source.</summary>
        public static bool TryGetLocalBounds(Component source, out Bounds bounds)
        {
            switch (source)
            {
                case null:
                    bounds = default;
                    return false;
                case Renderer renderer:
                    bounds = renderer.localBounds;
                    return true;
                case BoxCollider box:
                    bounds = new Bounds(box.center, box.size);
                    return true;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    bounds = mesh.sharedMesh.bounds;
                    return true;
                default:
                    bounds = new Bounds(Vector3.zero, Vector3.zero);
                    return true;
            }
        }

        /// <summary>Floor rectangle at the top face of <paramref name="local"/>.</summary>
        public static SceneFloorDto Floor(string id, Matrix4x4 localToWorld, Bounds local)
        {
            var top = localToWorld.MultiplyPoint3x4(local.center + Vector3.up * local.extents.y);
            var size = WorldSize(localToWorld, local.size);
            var rot = localToWorld.rotation;
            return new SceneFloorDto
            {
                id = id,
                px = top.x, py = top.y, pz = top.z,
                sx = size.x, sz = size.z,
                qx = rot.x, qy = rot.y, qz = rot.z, qw = rot.w
            };
        }

        /// <summary>Oriented box around <paramref name="local"/>, each axis at least
        /// <paramref name="minSize"/> so thin or point-like spots stay visible.</summary>
        public static SceneMarkerDto Marker(string id, string kind, string taskId,
            Matrix4x4 localToWorld, Bounds local, float minSize)
        {
            var centre = localToWorld.MultiplyPoint3x4(local.center);
            var size = Vector3.Max(WorldSize(localToWorld, local.size), Vector3.one * minSize);
            var rot = localToWorld.rotation;
            return new SceneMarkerDto
            {
                id = id, kind = kind, taskId = taskId,
                px = centre.x, py = centre.y, pz = centre.z,
                sx = size.x, sy = size.y, sz = size.z,
                qx = rot.x, qy = rot.y, qz = rot.z, qw = rot.w
            };
        }

        private static Vector3 WorldSize(Matrix4x4 localToWorld, Vector3 localSize)
        {
            var s = localToWorld.lossyScale;
            return new Vector3(Mathf.Abs(localSize.x * s.x), Mathf.Abs(localSize.y * s.y), Mathf.Abs(localSize.z * s.z));
        }
    }
}
