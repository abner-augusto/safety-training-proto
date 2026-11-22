using System;
using Oculus.Interaction;
using UnityEngine;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// Grab transformer for long, heavy pieces such as the toeboard, set as both the one-hand and
    /// the two-hand transformer of the piece's <see cref="Grabbable"/>. One hand drags the piece
    /// instead of carrying it: the free end sags to the floor and trails behind. Two hands lift
    /// it, its long axis following the line between them. The piece hangs from its grip either
    /// way, whatever its orientation when grabbed. The math is in <see cref="HeavyPieceGrabSolver"/>.
    /// </summary>
    public sealed class HeavyPieceTransformer : MonoBehaviour, ITransformer
    {
        [Tooltip("The piece's long axis, in its own space.")]
        [SerializeField] private Vector3 longAxis = Vector3.right;
        [Tooltip("The axis, in the piece's own space, that points up when it hangs from the handles.")]
        [SerializeField] private Vector3 upAxis = Vector3.up;
        [SerializeField] private HeavyPieceGrabSolver.Tuning tuning = HeavyPieceGrabSolver.Tuning.Default;
        [Tooltip("Surfaces the free end rests on. Keep the piece's own layer out.")]
        [SerializeField] private LayerMask floorLayers = 1;
        [Tooltip("How far below the grip a floor is looked for (m).")]
        [SerializeField, Min(0.1f)] private float floorSearchDepth = 6f;

        private IGrabbable _grabbable;
        private HeavyPieceGrabSolver _solver;
        private Func<Vector3, float?> _floorBelow;
        private int _hands;
        private float _lastStep;

        public void Initialize(IGrabbable grabbable)
        {
            _grabbable = grabbable;
            _solver = new HeavyPieceGrabSolver(longAxis, upAxis, LocalBounds(grabbable.Transform), tuning);
            _floorBelow = FloorBelow;
        }

        public void BeginTransform()
        {
            var points = _grabbable.GrabPoints;
            var target = _grabbable.Transform;
            var piece = new Pose(target.position, target.rotation);
            _hands = Mathf.Min(points.Count, 2);
            if (_hands == 1) _solver.BeginOneHand(piece, points[0]);
            else if (_hands == 2) _solver.BeginTwoHands(piece, points[0], points[1]);
            _lastStep = Time.time;
        }

        public void UpdateTransform()
        {
            var points = _grabbable.GrabPoints;
            if (_hands == 0 || points.Count < _hands) return;

            // Each hand raises its own move event, so a frame can step twice; only the first
            // one advances time.
            float dt = Time.time - _lastStep;
            _lastStep = Time.time;
            var pose = _hands == 1
                ? _solver.StepOneHand(points[0], dt, _floorBelow)
                : _solver.StepTwoHands(points[0], points[1], dt);
            _grabbable.Transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        public void EndTransform() => _hands = 0;

        private float? FloorBelow(Vector3 point)
        {
            return Physics.Raycast(point, Vector3.down, out var hit, floorSearchDepth, floorLayers,
                                   QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : (float?)null;
        }

        private static Bounds LocalBounds(Transform root)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (filter.sharedMesh == null) continue;
                var toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var mesh = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mesh.center + Vector3.Scale(mesh.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var local = toRoot.MultiplyPoint3x4(corner);
                    if (any) bounds.Encapsulate(local);
                    else { bounds = new Bounds(local, Vector3.zero); any = true; }
                }
            }
            return any ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (longAxis.sqrMagnitude < 1e-6f) longAxis = Vector3.right;
            if (Vector3.ProjectOnPlane(upAxis, longAxis).sqrMagnitude < 1e-6f) upAxis = Vector3.up;
        }
#endif
    }
}
