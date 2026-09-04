using UnityEngine;

namespace SafetyProto.Runtime.Safety
{
    /// <summary>
    /// Tuning for <see cref="ScaffoldCollapseSolver"/> and <see cref="ScaffoldCollapseSequence"/>.
    /// A plain value type so the MonoBehaviour can build one from its serialized fields each frame
    /// with no allocation, and tests can construct one inline. Mirrors the MenuFollowConfig pattern.
    /// </summary>
    public struct ScaffoldCollapseConfig
    {
        /// <summary>Total angle the scaffold tips through, in degrees.</summary>
        public float TiltAngleDegrees;

        /// <summary>Tilt angle at which the player loses the deck and the scripted fall takes over.</summary>
        public float DetachAngleDegrees;

        /// <summary>Comfort ceiling on the rotation the player rig inherits from the tipping
        /// structure. Past this the camera stops tilting further while the rig keeps following the
        /// pivot's translation, so the deck drops away without rolling the horizon past tolerance.</summary>
        public float MaxFollowTiltDegrees;

        /// <summary>Peak amplitude of the pre-collapse tremor, in degrees.</summary>
        public float TremorAmplitudeDegrees;

        /// <summary>Downward acceleration applied during the scripted fall, in m/s².</summary>
        public float FallGravity;

        /// <summary>Terminal speed of the scripted fall, in m/s. Caps how violent the drop reads
        /// before the fade covers it.</summary>
        public float MaxFallSpeed;
    }

    /// <summary>
    /// Pure maths behind the scaffold collapse. Every member is static and side-effect free, and
    /// nothing here touches a scene, a component, or time — the MonoBehaviour samples the
    /// AnimationCurve and passes the 0..1 result in, so tests never have to build a curve.
    /// See plans/027-scaffold-collapse-design.md for the beat sheet these functions serve.
    /// </summary>
    public static class ScaffoldCollapseSolver
    {
        // Perlin sampling rate for the tremor. High enough to read as a structural shudder rather
        // than a slow sway, low enough not to alias at 72 Hz.
        private const float TremorFrequency = 13f;

        /// <summary>Maps a 0..1 curve sample onto the configured tilt angle. Clamps, because
        /// AnimationCurve tangents routinely overshoot past 1 between keys.</summary>
        public static float TiltAngle(float curveValue01, in ScaffoldCollapseConfig cfg)
            => Mathf.Clamp01(curveValue01) * cfg.TiltAngleDegrees;

        /// <summary>True once the deck has tipped far enough that the player can no longer stand
        /// on it. Inclusive at the threshold.</summary>
        public static bool ShouldDetach(float tiltAngleDegrees, in ScaffoldCollapseConfig cfg)
            => tiltAngleDegrees >= cfg.DetachAngleDegrees;

        /// <summary>The rotation the rig actually inherits: the structure's tilt, saturated at the
        /// comfort ceiling.</summary>
        public static float FollowTiltDegrees(float tiltAngleDegrees, in ScaffoldCollapseConfig cfg)
            => Mathf.Min(tiltAngleDegrees, cfg.MaxFollowTiltDegrees);

        /// <summary>Signed tremor offset in degrees, ramping in from zero across
        /// <paramref name="duration"/>. Perlin-driven so replays are identical — the rehearsal
        /// button exists to compare runs.</summary>
        public static float TremorOffsetDegrees(float elapsed, float duration, in ScaffoldCollapseConfig cfg)
        {
            if (duration <= 0f) return 0f;
            float ramp = Mathf.Clamp01(elapsed / duration);
            float signed = Mathf.PerlinNoise(elapsed * TremorFrequency, 0f) * 2f - 1f;
            return signed * cfg.TremorAmplitudeDegrees * ramp;
        }

        /// <summary>One integration step of the scripted fall, clamped at terminal speed.</summary>
        public static float IntegrateFallSpeed(float currentSpeed, float deltaTime, in ScaffoldCollapseConfig cfg)
            => Mathf.Min(currentSpeed + cfg.FallGravity * deltaTime, cfg.MaxFallSpeed);
    }
}
