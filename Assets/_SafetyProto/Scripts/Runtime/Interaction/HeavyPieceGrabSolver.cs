using System;
using UnityEngine;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// Pose math of <see cref="HeavyPieceTransformer"/>, apart from the grab plumbing so it can be
    /// tested. A long piece held by one hand pivots on the grip: the free end sags until it rests
    /// on the floor, trails behind the hand like a dragged plank, and the wrist's yaw steers it
    /// only partly and late. Held by two hands, the long axis follows the line between them. Either
    /// way the piece swings round its long axis to hang from the grip, so the wrist's roll never
    /// turns it. Everything works in world space from the piece's own axes, so the piece behaves
    /// the same whether it was lying down or standing up when grabbed.
    /// </summary>
    public sealed class HeavyPieceGrabSolver
    {
        [Serializable]
        public struct Tuning
        {
            [Tooltip("How far below horizontal the free end sags when nothing holds it up (degrees).")]
            [Range(0f, 85f)] public float maxSagDegrees;
            [Tooltip("Time constant of the free end sagging (s).")]
            [Min(0f)] public float sagSeconds;
            [Tooltip("Time constant of the wrist's yaw reaching the piece held by one hand (s).")]
            [Min(0f)] public float yawSeconds;
            [Tooltip("Share of the wrist's yaw the piece follows when held by one hand.")]
            [Range(0f, 1f)] public float wristYawShare;
            [Tooltip("Time constant of the piece swinging round its long axis to hang from the grip (s).")]
            [Min(0f)] public float hangSeconds;

            public static Tuning Default => new Tuning
            {
                maxSagDegrees = 70f, sagSeconds = 0.45f, yawSeconds = 0.35f, wristYawShare = 0.6f, hangSeconds = 0.25f
            };
        }

        private const float MinLength = 0.05f;
        // Horizontal step between the points checked for something under the piece (m).
        private const float SupportSpacing = 0.08f;
        // Time constant of the shown pitch following the computed one, so a new contact eases
        // in over a few frames instead of snapping (s).
        private const float ContactSeconds = 0.08f;
        // A piece grabbed standing between supports keeps the hang it had within this distance of
        // the first grip, fading over the same distance again, instead of being tipped over at
        // the first frame (m).
        private const float BeginHangReach = 0.1f;
        // Height difference under which the surface below the tip counts as the one under the
        // last sample, rather than past an edge (m).
        private const float TipContinuity = 0.02f;
        // Below this horizontal share the wrist points up or down and its yaw is noise.
        private const float MinSteeringWeight = 0.3f;

        private readonly Vector3 _long;
        private readonly Vector3 _up;
        private readonly float _minAlong, _maxAlong, _minUp, _maxUp;
        private readonly Tuning _tuning;

        private Vector3 _gripLocal;
        private Vector3 _centroidLocal;
        private Vector3 _lineLocal;
        private Quaternion _beginRotation;
        private Quaternion _localBasis;
        private float _farSign;
        private float _farLength;
        private float _nearLength;
        private float _bottomBelowGrip;
        private float _hangSign;
        private Vector3 _heading;
        private Vector3 _farFlat;
        private float _pitch;
        private float _shownPitch;
        private Vector3 _upWorld;
        private float _wristYaw;
        private Vector3 _beginHand;
        private float _beginDepth;
        private readonly System.Collections.Generic.List<Vector2> _far = new System.Collections.Generic.List<Vector2>();
        private readonly System.Collections.Generic.List<Vector2> _near = new System.Collections.Generic.List<Vector2>();

        /// <param name="longAxis">The piece's long axis in its own space.</param>
        /// <param name="upAxis">The axis, in its own space, that points up when the piece hangs from
        /// a grip on its upper edge.</param>
        /// <param name="localBounds">The piece's extent in its own space.</param>
        public HeavyPieceGrabSolver(Vector3 longAxis, Vector3 upAxis, Bounds localBounds, Tuning tuning)
        {
            _long = longAxis.normalized;
            _up = Vector3.ProjectOnPlane(upAxis, _long).normalized;
            _tuning = tuning;
            _minAlong = _minUp = float.MaxValue;
            _maxAlong = _maxUp = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = localBounds.center + Vector3.Scale(localBounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                float along = Vector3.Dot(corner, _long), up = Vector3.Dot(corner, _up);
                _minAlong = Mathf.Min(_minAlong, along);
                _maxAlong = Mathf.Max(_maxAlong, along);
                _minUp = Mathf.Min(_minUp, up);
                _maxUp = Mathf.Max(_maxUp, up);
            }
        }

        /// <summary>Starts a one-hand hold from where the piece is now; the first step leaves it there.</summary>
        public void BeginOneHand(Pose piece, Pose grip)
        {
            _gripLocal = Quaternion.Inverse(piece.rotation) * (grip.position - piece.position);
            float along = Vector3.Dot(_gripLocal, _long);
            float toMax = _maxAlong - along, toMin = along - _minAlong;
            _farSign = toMax >= toMin ? 1f : -1f;
            _farLength = Mathf.Max(Mathf.Max(toMax, toMin), MinLength);
            _nearLength = Mathf.Max(Mathf.Min(toMax, toMin), 0f);
            SetHang(Vector3.Dot(_gripLocal, _up));

            Vector3 axis = piece.rotation * (_long * _farSign);
            _pitch = Mathf.Asin(Mathf.Clamp(-axis.y, -1f, 1f));
            _shownPitch = _pitch;
            _heading = Flat(axis);
            if (_heading.sqrMagnitude < 1e-8f) _heading = Flat(piece.rotation * _up);
            if (_heading.sqrMagnitude < 1e-8f) _heading = Vector3.forward;
            _heading.Normalize();
            _farFlat = Flat(grip.position) + _heading * (_farLength * Mathf.Cos(_pitch));
            _upWorld = piece.rotation * _up;
            _wristYaw = Yaw(grip.rotation, out _);
            _beginHand = grip.position;
            _beginDepth = _bottomBelowGrip * Mathf.Clamp01(_upWorld.y * _hangSign);
        }

        /// <summary>
        /// Moves the piece held by one hand. <paramref name="floorBelow"/> returns the height of the
        /// surface under a point given at the grip's height, or null when there is none.
        /// </summary>
        public Pose StepOneHand(Pose grip, float dt, Func<Vector3, float?> floorBelow)
        {
            Vector3 hand = grip.position;

            float yaw = Yaw(grip.rotation, out float weight);
            float turn = 0f;
            if (weight < MinSteeringWeight)
            {
                _wristYaw = yaw;
            }
            else
            {
                float step = Mathf.DeltaAngle(_wristYaw, yaw) * Blend(dt, _tuning.yawSeconds);
                _wristYaw += step;
                turn = step * _tuning.wristYawShare * weight;
            }

            // The free end stays where it lies unless the hand pulls it or the wrist steers it.
            Vector3 trail = Quaternion.AngleAxis(turn, Vector3.up) * (_farFlat - Flat(hand));
            if (trail.sqrMagnitude > 1e-8f) _heading = trail.normalized;

            float maxSag = _tuning.maxSagDegrees * Mathf.Deg2Rad;
            _pitch += (maxSag - _pitch) * Blend(dt, _tuning.sagSeconds);
            float hangLimit = float.PositiveInfinity;
            if (floorBelow != null) RestOnSupports(hand, floorBelow, maxSag, out hangLimit);
            else _pitch = Mathf.Clamp(_pitch, -maxSag, maxSag);

            // The supports bound the pitch at once, so it never settles into them; what is shown
            // follows a few frames behind, so a new contact does not snap.
            _shownPitch += (_pitch - _shownPitch) * Blend(dt, ContactSeconds);
            Vector3 axis = Axis(_heading, _shownPitch);
            _farFlat = Flat(hand) + _heading * (_farLength * Mathf.Cos(_shownPitch));
            Quaternion rotation = Hang(axis, dt, hangLimit);
            return new Pose(hand - rotation * _gripLocal, rotation);
        }

        /// <summary>Starts a two-hand hold from where the piece is now; the first step leaves it there.</summary>
        public void BeginTwoHands(Pose piece, Pose first, Pose second)
        {
            Quaternion inverse = Quaternion.Inverse(piece.rotation);
            Vector3 a = inverse * (first.position - piece.position), b = inverse * (second.position - piece.position);
            _centroidLocal = (a + b) * 0.5f;
            _lineLocal = b - a;
            _beginRotation = piece.rotation;
            _farSign = 1f;
            SetHang(Vector3.Dot(_centroidLocal, _up));
            _upWorld = piece.rotation * _up;
        }

        /// <summary>Moves the piece held by two hands; the grips' rotations are ignored.</summary>
        public Pose StepTwoHands(Pose first, Pose second, float dt)
        {
            Vector3 line = second.position - first.position;
            Vector3 lineAtBegin = _beginRotation * _lineLocal;
            Quaternion follow = line.sqrMagnitude > 1e-8f && lineAtBegin.sqrMagnitude > 1e-8f
                ? Quaternion.FromToRotation(lineAtBegin, line) * _beginRotation
                : _beginRotation;
            Quaternion rotation = Hang(follow * _long, dt, float.PositiveInfinity);
            Vector3 centroid = (first.position + second.position) * 0.5f;
            return new Pose(centroid - rotation * _centroidLocal, rotation);
        }

        // A grip above the middle of the piece's height leaves the up axis up as it hangs; one
        // below turns it down. Either way the lowest edge is the one farthest from the grip.
        private void SetHang(float gripUp)
        {
            _hangSign = gripUp >= (_minUp + _maxUp) * 0.5f ? 1f : -1f;
            _bottomBelowGrip = _hangSign > 0f ? gripUp - _minUp : _maxUp - gripUp;
            _localBasis = Quaternion.LookRotation(_long * _farSign, _up);
        }

        /// <param name="maxDepth">How far the lowest edge may drop under the grip line; the piece
        /// stops swinging to hang there, as on a table it lies on.</param>
        private Quaternion Hang(Vector3 axis, float dt, float maxDepth)
        {
            Vector3 vertical = Vector3.ProjectOnPlane(Vector3.up, axis);
            float verticalShare = vertical.magnitude;
            Vector3 hang = verticalShare > 1e-4f ? vertical / verticalShare * _hangSign : _upWorld;
            Vector3 target = hang;
            if (verticalShare > 1e-4f && _bottomBelowGrip > 1e-4f && !float.IsPositiveInfinity(maxDepth))
            {
                // cos of the swing towards hanging, capped so the lowest edge stays at maxDepth;
                // the piece tips towards the side it already leans to.
                float limit = Mathf.Clamp01(maxDepth / (_bottomBelowGrip * verticalShare));
                if (limit < 1f)
                {
                    Vector3 side = Vector3.Cross(axis, hang).normalized;
                    float sideSign = Vector3.Dot(_upWorld, side) >= 0f ? 1f : -1f;
                    target = hang * limit + side * (sideSign * Mathf.Sqrt(1f - limit * limit));
                }
            }
            Vector3 up = Vector3.Slerp(_upWorld, target, Blend(dt, _tuning.hangSeconds));
            up = Vector3.ProjectOnPlane(up, axis);
            _upWorld = up.sqrMagnitude > 1e-8f ? up.normalized : target;
            return Quaternion.LookRotation(axis, _upWorld) * Quaternion.Inverse(_localBasis);
        }

        /// <summary>
        /// Keeps the piece above whatever lies under it, on both sides of the grip: a plank lifted
        /// off one trestle still rests on the other, and one whose free end props on a bracket
        /// must not push its other end into the deck. Bounds the pitch, and, when the supports
        /// leave no room for the piece to hang from the grip (it lies on a table), how far it may.
        /// The supports are sampled at fixed distances, and each one bounds the pitch only if the
        /// piece still reaches over it at that pitch, so the result does not depend on the pitch
        /// of the frame before and cannot rock between two contacts.
        /// </summary>
        private void RestOnSupports(Vector3 hand, Func<Vector3, float?> floorBelow, float maxSag, out float maxDepth)
        {
            _far.Clear();
            _near.Clear();
            Sample(hand, _heading, _farLength, floorBelow, _far);
            Sample(hand, -_heading, _nearLength, floorBelow, _near);

            maxDepth = float.PositiveInfinity;
            float? under = floorBelow(hand);
            if (under.HasValue) maxDepth = hand.y - under.Value;
            // A support on each side fixes the pitch at which both are touched; the edge can drop
            // only as far as that pitch allows.
            foreach (var far in _far)
            foreach (var near in _near)
            {
                float d = ((hand.y - far.y) / far.x + (hand.y - near.y) / near.x) / (1f / far.x + 1f / near.x);
                float touching = Mathf.Atan2(hand.y - d - far.y, far.x);
                if (Reaches(far.x, _farLength, touching) && Reaches(near.x, _nearLength, touching))
                    maxDepth = Mathf.Min(maxDepth, d);
            }
            float travel = Vector3.Distance(hand, _beginHand);
            maxDepth = Mathf.Max(maxDepth, _beginDepth * Mathf.Clamp01(2f - travel / BeginHangReach), 0f);

            float depth = Mathf.Min(_bottomBelowGrip * Mathf.Clamp01(_upWorld.y * _hangSign), maxDepth);
            float edge = hand.y - depth;
            float lower = -maxSag, upper = maxSag;
            foreach (var far in _far)
            {
                float limit = Mathf.Atan2(edge - far.y, far.x);
                if (Reaches(far.x, _farLength, limit)) upper = Mathf.Min(upper, limit);
            }
            foreach (var near in _near)
            {
                float limit = Mathf.Atan2(near.y - edge, near.x);
                if (Reaches(near.x, _nearLength, limit)) lower = Mathf.Max(lower, limit);
            }
            // Between two samples the tip itself could dip in by up to the slope times a step;
            // where the surface under it continues that of the last sample it reaches, hold the
            // tip itself above it. At an edge the samples alone decide, which keeps it steady.
            float? farTip = TipSupport(hand, _heading, _farLength, floorBelow, _far);
            if (farTip.HasValue) upper = Mathf.Min(upper, Mathf.Asin(Mathf.Clamp((edge - farTip.Value) / _farLength, -1f, 1f)));
            float? nearTip = TipSupport(hand, -_heading, _nearLength, floorBelow, _near);
            if (nearTip.HasValue) lower = Mathf.Max(lower, Mathf.Asin(Mathf.Clamp((nearTip.Value - edge) / _nearLength, -1f, 1f)));
            _pitch = lower > upper ? (lower + upper) * 0.5f : Mathf.Clamp(_pitch, lower, upper);
        }

        private float? TipSupport(Vector3 hand, Vector3 direction, float length, Func<Vector3, float?> floorBelow,
                                  System.Collections.Generic.List<Vector2> samples)
        {
            float reach = length * Mathf.Cos(_pitch);
            if (length <= 1e-3f || reach <= 1e-3f) return null;
            Vector2? last = null;
            foreach (var sample in samples) if (sample.x <= reach) last = sample;
            if (!last.HasValue) return null;
            float? tip = floorBelow(hand + direction * reach);
            return tip.HasValue && Mathf.Abs(tip.Value - last.Value.y) < TipContinuity ? tip : null;
        }

        // Whether a side of the piece that long, tilted by that pitch, still lies over a point
        // that far out horizontally.
        private static bool Reaches(float distance, float length, float pitch) =>
            distance <= length * Mathf.Cos(pitch) + 1e-4f;

        // Support heights under one side of the grip as (horizontal distance, height), at fixed
        // steps out to the side's full length.
        private static void Sample(Vector3 hand, Vector3 direction, float length, Func<Vector3, float?> floorBelow,
                                   System.Collections.Generic.List<Vector2> into)
        {
            if (length <= 1e-3f) return;
            for (float d = SupportSpacing; ; d += SupportSpacing)
            {
                bool last = d >= length;
                if (last) d = length;
                float? floor = floorBelow(hand + direction * d);
                if (floor.HasValue) into.Add(new Vector2(d, floor.Value));
                if (last) return;
            }
        }

        private static Vector3 Axis(Vector3 heading, float pitch) =>
            heading * Mathf.Cos(pitch) + Vector3.down * Mathf.Sin(pitch);

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float Yaw(Quaternion rotation, out float weight)
        {
            Vector3 forward = Flat(rotation * Vector3.forward);
            weight = Mathf.Clamp01(forward.magnitude);
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private static float Blend(float dt, float seconds) =>
            seconds <= 0f ? 1f : 1f - Mathf.Exp(-Mathf.Max(dt, 0f) / seconds);
    }
}
