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

        /// <summary>Matrix that takes <see cref="TryGetLocalBounds"/>'s bounds to world space: a skinned
        /// renderer's bounds live in its root bone's space, every other source's in its own.</summary>
        public static Matrix4x4 BoundsToWorld(Component source) =>
            source is SkinnedMeshRenderer skinned && skinned.rootBone != null
                ? skinned.rootBone.localToWorldMatrix
                : source.transform.localToWorldMatrix;

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

        /// <summary>Distance kept from a floor's edge when a stand point is clamped onto it.</summary>
        public const float StandInset = 0.25f;

        /// <summary>How far outside a floor's rectangle a marker may sit and still be reached from it.</summary>
        public const float StandReach = 1.5f;

        /// <summary>
        /// Where a participant stands to work at <paramref name="id"/>: a floor id gives its top-face
        /// centre; a marker id gives the closest point, inset from the edge, of the highest floor
        /// not above the marker. <paramref name="lookAt"/> is the marker centre, or the stand point
        /// itself for a floor (no facing). False for an unknown id or a marker with no floor in reach.
        /// </summary>
        public static bool TryGetStandPoint(SceneLayoutDto layout, string id, out Vector3 standPoint, out Vector3 lookAt)
        {
            standPoint = lookAt = Vector3.zero;
            var floors = layout.floors ?? System.Array.Empty<SceneFloorDto>();

            foreach (var floor in floors)
            {
                if (floor.id != id) continue;
                standPoint = lookAt = new Vector3(floor.px, floor.py, floor.pz);
                return true;
            }

            foreach (var marker in layout.markers ?? System.Array.Empty<SceneMarkerDto>())
            {
                if (marker.id != id) continue;
                var target = new Vector3(marker.px, marker.py, marker.pz);

                bool found = false;
                SceneFloorDto best = default;
                Vector3 bestLocal = default;
                foreach (var floor in floors)
                {
                    if (floor.py > marker.py + 0.1f) continue;
                    var local = FloorLocal(floor, target);
                    if (Mathf.Abs(local.x) > floor.sx / 2f + StandReach || Mathf.Abs(local.z) > floor.sz / 2f + StandReach) continue;
                    if (found && floor.py <= best.py) continue;
                    found = true;
                    best = floor;
                    bestLocal = local;
                }
                if (!found) return false;

                float halfX = Mathf.Max(best.sx / 2f - StandInset, 0f);
                float halfZ = Mathf.Max(best.sz / 2f - StandInset, 0f);
                var clamped = new Vector3(Mathf.Clamp(bestLocal.x, -halfX, halfX), 0f, Mathf.Clamp(bestLocal.z, -halfZ, halfZ));
                standPoint = new Vector3(best.px, best.py, best.pz) + FloorRotation(best) * clamped;
                lookAt = target;
                return true;
            }

            return false;
        }

        private static Quaternion FloorRotation(SceneFloorDto floor) => new Quaternion(floor.qx, floor.qy, floor.qz, floor.qw);

        private static Vector3 FloorLocal(SceneFloorDto floor, Vector3 world) =>
            Quaternion.Inverse(FloorRotation(floor)) * (world - new Vector3(floor.px, floor.py, floor.pz));

        private static Vector3 WorldSize(Matrix4x4 localToWorld, Vector3 localSize)
        {
            var s = localToWorld.lossyScale;
            return new Vector3(Mathf.Abs(localSize.x * s.x), Mathf.Abs(localSize.y * s.y), Mathf.Abs(localSize.z * s.z));
        }
    }
}
