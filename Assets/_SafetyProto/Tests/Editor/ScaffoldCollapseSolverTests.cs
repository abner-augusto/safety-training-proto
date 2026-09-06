// Assets/_SafetyProto/Tests/Editor/ScaffoldCollapseSolverTests.cs
//
// Unity EditMode only (Mathf, no scene, no headset). Exercises the pure maths behind
// ScaffoldCollapseSequence the same way MenuFollowSolverTests exercises MenuFollowSolver:
// the tilt ramp, the detach threshold, the comfort clamp on inherited camera rotation,
// the tremor envelope, and the terminal-speed clamp on the scripted fall.
using NUnit.Framework;
using SafetyProto.Runtime.Safety;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class ScaffoldCollapseSolverTests
    {
        private static ScaffoldCollapseConfig DefaultConfig() => new ScaffoldCollapseConfig
        {
            TiltAngleDegrees = 55f,
            DetachAngleDegrees = 30f,
            MaxFollowTiltDegrees = 22f,
            TremorAmplitudeDegrees = 0.5f,
            FallGravity = 9.81f,
            MaxFallSpeed = 12f,
        };

        [Test]
        public void TiltAngle_MapsCurveRangeOntoConfiguredAngle()
        {
            var cfg = DefaultConfig();
            Assert.AreEqual(0f, ScaffoldCollapseSolver.TiltAngle(0f, cfg), 1e-4f);
            Assert.AreEqual(27.5f, ScaffoldCollapseSolver.TiltAngle(0.5f, cfg), 1e-4f);
            Assert.AreEqual(55f, ScaffoldCollapseSolver.TiltAngle(1f, cfg), 1e-4f);
        }

        [Test]
        public void TiltAngle_ClampsCurveOvershootAndUndershoot()
        {
            // AnimationCurve tangents routinely overshoot past 1 between keys; the scaffold
            // must never tip further than the authored angle because of curve shape.
            var cfg = DefaultConfig();
            Assert.AreEqual(55f, ScaffoldCollapseSolver.TiltAngle(1.4f, cfg), 1e-4f);
            Assert.AreEqual(0f, ScaffoldCollapseSolver.TiltAngle(-0.3f, cfg), 1e-4f);
        }

        [Test]
        public void ShouldDetach_IsInclusiveAtTheThreshold()
        {
            var cfg = DefaultConfig();
            Assert.IsFalse(ScaffoldCollapseSolver.ShouldDetach(29.9f, cfg));
            Assert.IsTrue(ScaffoldCollapseSolver.ShouldDetach(30f, cfg));
            Assert.IsTrue(ScaffoldCollapseSolver.ShouldDetach(55f, cfg));
        }

        [Test]
        public void FollowTiltDegrees_TracksTiltThenSaturatesAtTheComfortCeiling()
        {
            var cfg = DefaultConfig();
            Assert.AreEqual(10f, ScaffoldCollapseSolver.FollowTiltDegrees(10f, cfg), 1e-4f);
            Assert.AreEqual(22f, ScaffoldCollapseSolver.FollowTiltDegrees(22f, cfg), 1e-4f);
            Assert.AreEqual(22f, ScaffoldCollapseSolver.FollowTiltDegrees(55f, cfg), 1e-4f);
        }

        [Test]
        public void TremorOffset_ActuallyShakes_AndStaysInsideAmplitude()
        {
            var cfg = DefaultConfig();
            Assert.AreEqual(0f, ScaffoldCollapseSolver.TremorOffsetDegrees(0f, 1.2f, cfg), 1e-4f);

            // Bounds alone are satisfied by a tremor that never leaves zero, which would mean
            // no pre-collapse shake at all. The series has to be shown to move.
            float peak = 0f;
            for (float t = 0f; t <= 1.2f; t += 0.01f)
            {
                float offset = ScaffoldCollapseSolver.TremorOffsetDegrees(t, 1.2f, cfg);
                Assert.LessOrEqual(Mathf.Abs(offset), cfg.TremorAmplitudeDegrees + 1e-4f,
                    $"tremor left its envelope at t={t}");
                peak = Mathf.Max(peak, Mathf.Abs(offset));
            }

            Assert.Greater(peak, cfg.TremorAmplitudeDegrees * 0.25f,
                "tremor never moved far from zero — the scaffold is not shaking before it tips");
        }

        [Test]
        public void TremorOffset_IsDeterministic()
        {
            // The rehearsal button replays the sequence repeatedly; a tremor driven by
            // Random would make two runs impossible to compare.
            var cfg = DefaultConfig();
            Assert.AreEqual(
                ScaffoldCollapseSolver.TremorOffsetDegrees(0.7f, 1.2f, cfg),
                ScaffoldCollapseSolver.TremorOffsetDegrees(0.7f, 1.2f, cfg));
        }

        [Test]
        public void TremorOffset_IsZeroForZeroDuration()
        {
            var cfg = DefaultConfig();
            Assert.AreEqual(0f, ScaffoldCollapseSolver.TremorOffsetDegrees(0.5f, 0f, cfg), 1e-4f);
        }

        [Test]
        public void IntegrateFallSpeed_AccumulatesGravityThenClampsAtTerminalSpeed()
        {
            var cfg = DefaultConfig();
            float dt = 1f / 72f;

            Assert.AreEqual(9.81f * dt, ScaffoldCollapseSolver.IntegrateFallSpeed(0f, dt, cfg), 1e-4f);

            float speed = 0f;
            for (int i = 0; i < 500; i++)
                speed = ScaffoldCollapseSolver.IntegrateFallSpeed(speed, dt, cfg);

            Assert.AreEqual(cfg.MaxFallSpeed, speed, 1e-4f);
        }
    }
}
