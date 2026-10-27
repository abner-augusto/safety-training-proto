using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Decides when a body position has left or come back to a rectangular walkable zone. Pure:
    /// positions are in the zone's own frame (x = along its width, y = along its depth, origin at
    /// its center). Leaving needs <c>exitDelay</c> seconds outside, so a lean or one stray frame
    /// does not fire; coming back needs <c>reentryDepth</c> meters inside the edge, so standing
    /// on the edge does not flicker.
    /// </summary>
    public sealed class WalkableZoneTracker
    {
        public enum Transition { None, Left, Returned }

        private readonly Vector2 _halfSize;
        private readonly float _exitDelay;
        private readonly float _reentryDepth;
        private float _pendingExit;

        public bool IsOutside { get; private set; }

        /// <summary>Seconds since the body first stepped out, including the exit delay.</summary>
        public float OutsideSeconds { get; private set; }

        public WalkableZoneTracker(Vector2 size, float exitDelay, float reentryDepth)
        {
            _halfSize = size * 0.5f;
            _exitDelay = Mathf.Max(0f, exitDelay);
            _reentryDepth = Mathf.Max(0f, reentryDepth);
        }

        public Transition Step(Vector2 bodyInZone, float deltaTime)
        {
            if (!IsOutside)
            {
                if (IsInside(bodyInZone, 0f))
                {
                    _pendingExit = 0f;
                    return Transition.None;
                }

                _pendingExit += deltaTime;
                if (_pendingExit < _exitDelay) return Transition.None;

                IsOutside = true;
                OutsideSeconds = _pendingExit;
                return Transition.Left;
            }

            OutsideSeconds += deltaTime;
            if (!IsInside(bodyInZone, _reentryDepth)) return Transition.None;

            IsOutside = false;
            _pendingExit = 0f;
            return Transition.Returned;
        }

        /// <summary>Forgets any exit in progress without reporting a return.</summary>
        public void Reset()
        {
            IsOutside = false;
            OutsideSeconds = 0f;
            _pendingExit = 0f;
        }

        /// <summary>
        /// Where to send someone standing at <paramref name="bodyInZone"/>: the zone's center line,
        /// at the closest point along its width that counts as back inside.
        /// </summary>
        public Vector2 NearestSafePoint(Vector2 bodyInZone)
        {
            float limit = Mathf.Max(0f, _halfSize.x - _reentryDepth);
            return new Vector2(Mathf.Clamp(bodyInZone.x, -limit, limit), 0f);
        }

        private bool IsInside(Vector2 p, float inset) =>
            Mathf.Abs(p.x) <= _halfSize.x - inset && Mathf.Abs(p.y) <= _halfSize.y - inset;
    }
}
