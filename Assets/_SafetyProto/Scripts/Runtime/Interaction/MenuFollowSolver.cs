using UnityEngine;

namespace SafetyProto.Runtime.Interaction
{
    /// <summary>
    /// Tuning for <see cref="MenuFollowSolver"/>. A plain value type so the MonoBehaviour can build
    /// one from its serialized fields each frame with no allocation, and tests can construct one
    /// inline.
    /// </summary>
    public struct MenuFollowConfig
    {
        /// <summary>Distance in metres the menu floats from the head along the follow direction.</summary>
        public float FollowDistance;

        /// <summary>Extra offset applied in head-local space (free mode) or as a height delta
        /// (<see cref="YawOnly"/> uses only <c>.y</c>).</summary>
        public Vector3 MenuOffset;

        /// <summary>Metres of head translation drift tolerated before the follow engages.</summary>
        public float PositionDeadzone;

        /// <summary>Half-angle of the horizontal comfort cone. The menu stays put until the head
        /// forward is more than this many degrees off the menu.</summary>
        public float YawDeadzoneDegrees;

        /// <summary>Half-angle of the vertical comfort cone. Ignored when <see cref="YawOnly"/>.</summary>
        public float PitchDeadzoneDegrees;

        /// <summary>Seconds the head must stay outside the comfort zone before the follow engages.
        /// A quick glance that returns within this window never moves the menu. 0 = immediate.</summary>
        public float DwellBeforeFollow;

        /// <summary><see cref="Vector3.SmoothDamp"/> time for position catch-up.</summary>
        public float PositionSmoothTime;

        /// <summary><see cref="Mathf.SmoothDampAngle"/> time for the facing yaw.</summary>
        public float RotationSmoothTime;

        /// <summary>Fraction of the comfort-cone half-angle the follow re-centres the menu to once
        /// engaged (0.25 => a "settle ring" a quarter of the way out, i.e. near but not at the
        /// gaze centre). The follow relaxes when it reaches that ring, so this doubles as the
        /// hysteresis margin against a head parked on the engage threshold. 0 = re-centre fully.</summary>
        public float ReengageFraction;

        /// <summary>When true the menu rides a horizontal ring at a fixed world height and only
        /// orbits the user in yaw; head pitch and vertical bob never move it.</summary>
        public bool YawOnly;

        /// <summary>Clamp values that would otherwise misbehave (negative times, cone &gt; 90 deg).</summary>
        public MenuFollowConfig Sanitized()
        {
            var c = this;
            c.FollowDistance = Mathf.Max(0.01f, c.FollowDistance);
            c.PositionDeadzone = Mathf.Max(0f, c.PositionDeadzone);
            c.YawDeadzoneDegrees = Mathf.Clamp(c.YawDeadzoneDegrees, 0f, 89f);
            c.PitchDeadzoneDegrees = Mathf.Clamp(c.PitchDeadzoneDegrees, 0f, 89f);
            c.DwellBeforeFollow = Mathf.Max(0f, c.DwellBeforeFollow);
            c.PositionSmoothTime = Mathf.Max(0.01f, c.PositionSmoothTime);
            c.RotationSmoothTime = Mathf.Max(0.01f, c.RotationSmoothTime);
            c.ReengageFraction = Mathf.Clamp01(c.ReengageFraction);
            return c;
        }
    }

    /// <summary>
    /// A position + rotation pair, passed in and out of <see cref="MenuFollowSolver.Tick"/>.
    /// </summary>
    public struct MenuPose
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public MenuPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Lazy-follow ("tag-along") solver for a head-anchored menu. Engine-light on purpose — it uses
    /// <see cref="UnityEngine"/> vector maths but no components, coroutines or scene, so the whole
    /// behaviour is unit-testable with an injected delta time (see <c>MenuFollowSolverTests</c>).
    ///
    /// The model, mirroring Unity XRI <c>LazyFollow</c> and the MRTK <c>Follow</c> solver:
    /// <list type="bullet">
    /// <item>A <b>comfort cone</b> (yaw/pitch half-angles) plus a translation deadzone define where
    /// the head may move without the menu reacting.</item>
    /// <item>A <b>dwell timer</b> gates engagement: the head must sit outside the comfort zone for
    /// <see cref="MenuFollowConfig.DwellBeforeFollow"/> seconds continuously before the menu starts
    /// catching up. Quick glances are ignored.</item>
    /// <item>Once engaged the menu is eased toward an inner <b>settle ring</b>
    /// (<see cref="MenuFollowConfig.ReengageFraction"/> of the cone half-angle) and to
    /// <see cref="MenuFollowConfig.FollowDistance"/> — near the gaze centre but not dead centre,
    /// and never glued to the FOV edge.</item>
    /// <item>The follow <b>relaxes</b> once it reaches that ring, so a head parked on the engage
    /// threshold cannot toggle it on and off.</item>
    /// <item>Facing always eases toward the user and stays upright (no roll, no pitch).</item>
    /// </list>
    /// </summary>
    public sealed class MenuFollowSolver
    {
        private bool _following;
        private float _dwellElapsed;
        private Vector3 _positionVelocity;
        private float _yawVelocity;

        private float _lockedHeight;
        private bool _hasLockedHeight;

        // Last valid flattened head-forward, reused when the head looks straight up or down and the
        // projection onto the horizontal plane is degenerate.
        private Vector3 _lastFlatForward = Vector3.forward;

        // SmoothDamp only approaches its target asymptotically, so the disengage test needs a small
        // tolerance on top of the settle ring / deadzone fraction to ever fire.
        private const float AngleSettleEpsilonDegrees = 0.5f;
        private const float DistanceSettleEpsilonMetres = 0.01f;

        /// <summary>True while the menu is actively catching up to the head.</summary>
        public bool IsFollowing => _following;

        /// <summary>Dwell progress in the 0..1 range while waiting to engage; suitable for a debug readout.</summary>
        public float DwellProgress => _config.DwellBeforeFollow <= 0f
            ? (_following ? 1f : 0f)
            : Mathf.Clamp01(_dwellElapsed / _config.DwellBeforeFollow);

        private MenuFollowConfig _config;

        /// <summary>
        /// Pins the world height used by <see cref="MenuFollowConfig.YawOnly"/> mode. Call with the
        /// current head Y when the menu is shown or recentred.
        /// </summary>
        public void SnapHeight(float worldHeight)
        {
            _lockedHeight = worldHeight;
            _hasLockedHeight = true;
        }

        /// <summary>Clears engagement, the dwell timer and the smoothing velocities.</summary>
        public void Reset()
        {
            _following = false;
            _dwellElapsed = 0f;
            _positionVelocity = Vector3.zero;
            _yawVelocity = 0f;
        }

        /// <summary>
        /// Advances the follow by <paramref name="deltaTime"/> seconds and returns the menu's next
        /// pose. <paramref name="current"/> is the menu's pose right now.
        /// </summary>
        public MenuPose Tick(MenuPose current, Vector3 headPosition, Quaternion headRotation,
            MenuFollowConfig config, float deltaTime)
        {
            _config = config.Sanitized();
            if (!_hasLockedHeight)
                SnapHeight(headPosition.y);

            UpdateFlatForward(headRotation * Vector3.forward);

            bool outside = IsOutsideComfortZone(current.Position, headPosition, headRotation);

            if (!_following)
            {
                if (outside)
                {
                    _dwellElapsed += deltaTime;
                    if (_dwellElapsed >= _config.DwellBeforeFollow)
                    {
                        _following = true;
                        _positionVelocity = Vector3.zero;
                    }
                }
                else
                {
                    _dwellElapsed = 0f;
                }
            }

            Vector3 nextPosition = current.Position;
            if (_following)
            {
                // Ease toward the inner settle ring (and back to follow distance) — not the outer
                // cone edge, so the follow can actually reach its target and relax rather than
                // trailing the head forever.
                Vector3 positionTarget = SolveSettleTarget(current.Position, headPosition, headRotation);

                nextPosition = Vector3.SmoothDamp(current.Position, positionTarget,
                    ref _positionVelocity, _config.PositionSmoothTime, Mathf.Infinity, deltaTime);

                if (HasSettled(nextPosition, headPosition, headRotation))
                {
                    _following = false;
                    _dwellElapsed = 0f;
                    _positionVelocity = Vector3.zero;
                }
            }

            Quaternion nextRotation = EaseFacing(current.Rotation, nextPosition, headPosition, deltaTime);
            return new MenuPose(nextPosition, nextRotation);
        }

        /// <summary>
        /// The pose the menu should snap to when shown or recentred: squarely in front of the head
        /// at <see cref="MenuFollowConfig.FollowDistance"/>, facing the user and upright.
        /// </summary>
        public static MenuPose ComputeIdealPose(Vector3 headPosition, Quaternion headRotation,
            MenuFollowConfig config)
        {
            return ComputeIdealPose(headPosition, headRotation, config, headPosition.y);
        }

        /// <summary>
        /// <see cref="ComputeIdealPose(Vector3, Quaternion, MenuFollowConfig)"/> with an explicit
        /// locked world height for <see cref="MenuFollowConfig.YawOnly"/> mode.
        /// </summary>
        public static MenuPose ComputeIdealPose(Vector3 headPosition, Quaternion headRotation,
            MenuFollowConfig config, float lockedHeight)
        {
            config = config.Sanitized();
            Vector3 headForward = headRotation * Vector3.forward;

            Vector3 position;
            if (config.YawOnly)
            {
                Vector3 flat = Vector3.ProjectOnPlane(headForward, Vector3.up);
                flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
                position = new Vector3(headPosition.x, lockedHeight + config.MenuOffset.y, headPosition.z)
                           + flat * config.FollowDistance;
            }
            else
            {
                position = headPosition
                           + headForward * config.FollowDistance
                           + headRotation * config.MenuOffset;
            }

            return new MenuPose(position, FacingRotation(position, headPosition));
        }

        private void UpdateFlatForward(Vector3 headForward)
        {
            Vector3 flat = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f)
                _lastFlatForward = flat.normalized;
        }

        /// <summary>
        /// How far the menu is from <see cref="MenuFollowConfig.FollowDistance"/>. Measured on the
        /// horizontal plane in yaw-only mode so crouching or a vertical head bob does not read as
        /// drift.
        /// </summary>
        private float DistanceError(Vector3 menuPosition, Vector3 headPosition)
        {
            Vector3 toMenu = menuPosition - headPosition;
            if (_config.YawOnly)
                toMenu = Vector3.ProjectOnPlane(toMenu, Vector3.up);
            return Mathf.Abs(toMenu.magnitude - _config.FollowDistance);
        }

        /// <summary>
        /// Where the menu sits relative to the head's gaze, split into a signed yaw (right +, left
        /// -) and pitch (up +, down -) in degrees. Yaw-only mode always reports pitch 0 and
        /// measures yaw on the horizontal plane.
        /// </summary>
        private void HeadLocalAngles(Vector3 menuPosition, Vector3 headPosition, Quaternion headRotation,
            out float yaw, out float pitch)
        {
            yaw = 0f;
            pitch = 0f;
            Vector3 toMenu = menuPosition - headPosition;

            if (_config.YawOnly)
            {
                Vector3 flatToMenu = Vector3.ProjectOnPlane(toMenu, Vector3.up);
                if (flatToMenu.sqrMagnitude > 1e-6f)
                    yaw = Vector3.SignedAngle(_lastFlatForward, flatToMenu.normalized, Vector3.up);
                return;
            }

            if (toMenu.sqrMagnitude <= 1e-6f) return;
            Vector3 local = (Quaternion.Inverse(headRotation) * toMenu.normalized);
            yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private bool IsOutsideComfortZone(Vector3 menuPosition, Vector3 headPosition, Quaternion headRotation)
        {
            HeadLocalAngles(menuPosition, headPosition, headRotation, out float yaw, out float pitch);
            if (Mathf.Abs(yaw) > _config.YawDeadzoneDegrees) return true;
            if (!_config.YawOnly && Mathf.Abs(pitch) > _config.PitchDeadzoneDegrees) return true;
            return DistanceError(menuPosition, headPosition) > _config.PositionDeadzone;
        }

        private bool HasSettled(Vector3 menuPosition, Vector3 headPosition, Quaternion headRotation)
        {
            HeadLocalAngles(menuPosition, headPosition, headRotation, out float yaw, out float pitch);
            if (Mathf.Abs(yaw) > YawSettleAngle() + AngleSettleEpsilonDegrees) return false;
            if (!_config.YawOnly && Mathf.Abs(pitch) > PitchSettleAngle() + AngleSettleEpsilonDegrees)
                return false;
            float distanceThreshold = Mathf.Max(
                _config.PositionDeadzone * _config.ReengageFraction, DistanceSettleEpsilonMetres);
            return DistanceError(menuPosition, headPosition) <= distanceThreshold;
        }

        private float YawSettleAngle() => _config.YawDeadzoneDegrees * _config.ReengageFraction;
        private float PitchSettleAngle() => _config.PitchDeadzoneDegrees * _config.ReengageFraction;

        /// <summary>
        /// The position the follow eases toward while engaged: the menu's current yaw/pitch offset
        /// from the gaze, each clamped in to its settle ring, re-projected to follow distance. An
        /// offset already inside its ring is left untouched (no chase to dead centre); only the
        /// distance is re-imposed.
        /// </summary>
        private Vector3 SolveSettleTarget(Vector3 menuPosition, Vector3 headPosition, Quaternion headRotation)
        {
            HeadLocalAngles(menuPosition, headPosition, headRotation, out float yaw, out float pitch);

            if (_config.YawOnly)
            {
                float yawTarget = Mathf.Clamp(yaw, -YawSettleAngle(), YawSettleAngle());
                Vector3 dir = Quaternion.AngleAxis(yawTarget, Vector3.up) * _lastFlatForward;
                float height = _lockedHeight + _config.MenuOffset.y;
                return new Vector3(headPosition.x, height, headPosition.z) + dir * _config.FollowDistance;
            }

            float yawClamped = Mathf.Clamp(yaw, -YawSettleAngle(), YawSettleAngle());
            float pitchClamped = Mathf.Clamp(pitch, -PitchSettleAngle(), PitchSettleAngle());
            Vector3 localDir = Quaternion.Euler(-pitchClamped, yawClamped, 0f) * Vector3.forward;
            Vector3 worldDir = headRotation * localDir;
            return headPosition + worldDir * _config.FollowDistance + headRotation * _config.MenuOffset;
        }

        private Quaternion EaseFacing(Quaternion current, Vector3 menuPosition, Vector3 headPosition,
            float deltaTime)
        {
            Quaternion target = FacingRotation(menuPosition, headPosition);
            float currentYaw = current.eulerAngles.y;
            float targetYaw = target.eulerAngles.y;
            float yaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref _yawVelocity,
                _config.RotationSmoothTime, Mathf.Infinity, deltaTime);
            return Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// Rotation that makes the panel's forward point from the head toward the panel (so its
        /// reading face is toward the user), kept upright — no roll, no pitch.
        /// </summary>
        private static Quaternion FacingRotation(Vector3 menuPosition, Vector3 headPosition)
        {
            Vector3 lookDirection = menuPosition - headPosition;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude <= 1e-6f)
                return Quaternion.identity;
            return Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }
    }
}
