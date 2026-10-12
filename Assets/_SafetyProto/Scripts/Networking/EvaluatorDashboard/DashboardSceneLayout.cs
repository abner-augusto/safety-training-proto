using System;
using System.Collections.Generic;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Runtime.Simulation;
using UnityEngine;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>What a dashboard task marker represents; sent as its name.</summary>
    public enum DashboardMarkerKind
    {
        Guardrail,
        Toeboard,
        Anchor,
        Irregularity
    }

    /// <summary>
    /// Authoring list of the walkable floors and task spots the evaluator dashboard draws.
    /// One per scene; <see cref="EvaluatorDashboardBootstrap"/> looks it up on every
    /// RequestSync because the bootstrap outlives scene reloads and this component does not.
    /// </summary>
    public class DashboardSceneLayout : MonoBehaviour, ISimulationWaypoints
    {
        /// <summary>Smallest drawn extent per axis when an entry leaves minSize at 0.</summary>
        public const float DefaultMinSize = 0.12f;

        [Serializable]
        public struct FloorEntry
        {
            public string id;
            [Tooltip("Renderer, BoxCollider or MeshCollider whose top face is the walkable floor.")]
            public Component source;
        }

        [Serializable]
        public struct MarkerEntry
        {
            public string id;
            public DashboardMarkerKind kind;
            [Tooltip("Scenario task id whose status colours this marker (e.g. install_guardrail).")]
            public string taskId;
            [Tooltip("Renderer or Collider of the task spot; any other component draws a point.")]
            public Component source;
            [Tooltip("Minimum drawn size per axis, in metres. 0 uses the default.")]
            [Min(0f)] public float minSize;
        }

        [SerializeField] private FloorEntry[] floors = Array.Empty<FloorEntry>();
        [SerializeField] private MarkerEntry[] markers = Array.Empty<MarkerEntry>();

        private SceneLayoutDto? _cached;

        /// <summary>The layout DTO, captured at scene start. Install spots do not move, so it is cached.</summary>
        public SceneLayoutDto Build() => _cached ??= BuildFrom(floors, markers, this);

        // Capture before anything moves: the scaffold collapse tilts the deck, and a dashboard
        // connecting after it would otherwise cache a tilted floor the yaw-only lookup misreads.
        private void Start() => Build();

        /// <summary>Floor ids, then marker ids: the session simulator's move targets.</summary>
        public IReadOnlyList<string> WaypointIds
        {
            get
            {
                var layout = Build();
                var ids = new List<string>();
                foreach (var floor in layout.floors) ids.Add(floor.id);
                foreach (var marker in layout.markers) ids.Add(marker.id);
                return ids;
            }
        }

        public bool TryGetStandPoint(string id, out Vector3 standPoint, out Vector3 lookAt) =>
            SceneLayoutGeometry.TryGetStandPoint(Build(), id, out standPoint, out lookAt);

        public static SceneLayoutDto BuildFrom(FloorEntry[] floors, MarkerEntry[] markers, UnityEngine.Object logContext)
        {
            var floorDtos = new List<SceneFloorDto>();
            foreach (var entry in floors ?? Array.Empty<FloorEntry>())
            {
                if (!SceneLayoutGeometry.TryGetLocalBounds(entry.source, out var bounds))
                {
                    SafetyLog.Warning($"[DashboardSceneLayout] Floor '{entry.id}' has no source; skipped.", logContext);
                    continue;
                }
                var floor = SceneLayoutGeometry.Floor(entry.id, SceneLayoutGeometry.BoundsToWorld(entry.source), bounds);
                if (floor.sx < 1e-3f || floor.sz < 1e-3f)
                {
                    SafetyLog.Warning($"[DashboardSceneLayout] Floor '{entry.id}' has no area (point or line source); skipped. " +
                                      "Point it at a Renderer or Collider.", logContext);
                    continue;
                }
                floorDtos.Add(floor);
            }

            var markerDtos = new List<SceneMarkerDto>();
            foreach (var entry in markers ?? Array.Empty<MarkerEntry>())
            {
                if (!SceneLayoutGeometry.TryGetLocalBounds(entry.source, out var bounds))
                {
                    SafetyLog.Warning($"[DashboardSceneLayout] Marker '{entry.id}' has no source; skipped.", logContext);
                    continue;
                }
                var minSize = entry.minSize > 0f ? entry.minSize : DefaultMinSize;
                markerDtos.Add(SceneLayoutGeometry.Marker(entry.id, entry.kind.ToString(), entry.taskId,
                    SceneLayoutGeometry.BoundsToWorld(entry.source), bounds, minSize));
            }

            return new SceneLayoutDto { floors = floorDtos.ToArray(), markers = markerDtos.ToArray() };
        }

#if UNITY_EDITOR
        // Dropping a GameObject on a Component field assigns its Transform, which would turn a
        // floor into a zero-size point. Prefer the Renderer, then any Collider, on that object.
        private void OnValidate()
        {
            for (int i = 0; i < floors.Length; i++) floors[i].source = Upgrade(floors[i].source);
            for (int i = 0; i < markers.Length; i++) markers[i].source = Upgrade(markers[i].source);
            _cached = null;
        }

        private static Component Upgrade(Component source)
        {
            if (!(source is Transform t)) return source;
            if (t.TryGetComponent<Renderer>(out var renderer)) return renderer;
            if (t.TryGetComponent<Collider>(out var collider)) return collider;
            return source;
        }
#endif
    }
}
