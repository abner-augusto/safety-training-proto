// Assets/_SafetyProto/Tests/Editor/MenuFollowSolverTests.cs
//
// Unity EditMode only (Vector3/Quaternion/Mathf, no scene, no headset). Drives MenuFollowSolver
// frame by frame with an injected delta time, the same way PlayerRecenterTests exercises the pure
// PlayerRecenter.Solve. Covers the lazy-follow contract: no motion on a quick glance, engage only
// after the dwell, re-centre to the settle ring (not dead centre, not the FOV edge), the yaw-only
// height lock, and no drift at rest.
using System;
using NUnit.Framework;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class MenuFollowSolverTests
    {
        private const float FrameDt = 1f / 72f;

        private static MenuFollowConfig DefaultConfig() => new MenuFollowConfig
        {
            FollowDistance = 0.5f,
            MenuOffset = Vector3.zero,
            PositionDeadzone = 0.15f,
            YawDeadzoneDegrees = 12f,
            PitchDeadzoneDegrees = 18f,
            DwellBeforeFollow = 0.35f,
            PositionSmoothTime = 0.25f,
            RotationSmoothTime = 0.15f,
            ReengageFraction = 0.25f,
            YawOnly = false,
        };

        private static MenuPose Run(MenuFollowSolver solver, MenuPose pose, MenuFollowConfig cfg,
            Vector3 headPosition, Quaternion headRotation, float seconds)
        {
            return Run(solver, pose, cfg, _ => (headPosition, headRotation), seconds);
        }

        private static MenuPose Run(MenuFollowSolver solver, MenuPose pose, MenuFollowConfig cfg,
            Func<float, (Vector3 pos, Quaternion rot)> head, float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / FrameDt);
            for (int i = 0; i < steps; i++)
            {
                var h = head(i * FrameDt);
                pose = solver.Tick(pose, h.pos, h.rot, cfg, FrameDt);
            }
            return pose;
        }

        [Test]
        public void AtRest_MenuDoesNotDrift()
        {
            var cfg = DefaultConfig();
            var start = MenuFollowSolver.ComputeIdealPose(Vector3.zero, Quaternion.identity, cfg);
            var solver = new MenuFollowSolver();
            solver.SnapHeight(0f);

            var end = Run(solver, start, cfg, Vector3.zero, Quaternion.identity, 2f);

            Assert.Less(Vector3.Distance(end.Position, start.Position), 1e-4f);
            Assert.Less(Quaternion.Angle(end.Rotation, start.Rotation), 0.05f);
            Assert.IsFalse(solver.IsFollowing);
        }

        [Test]
        public void QuickGlance_ShorterThanDwell_DoesNotMoveMenu()
        {
            var cfg = DefaultConfig();
            var start = MenuFollowSolver.ComputeIdealPose(Vector3.zero, Quaternion.identity, cfg);
            var solver = new MenuFollowSolver();
            solver.SnapHeight(0f);

            // 25 deg off (outside the 12 deg cone) but only for 0.2 s, then back — never reaches
            // the 0.35 s dwell.
            var end = Run(solver, start, cfg,
                t => (Vector3.zero, t < 0.2f ? Quaternion.Euler(0f, 25f, 0f) : Quaternion.identity),
                1f);

            Assert.Less(Vector3.Distance(end.Position, start.Position), 1e-4f);
            Assert.IsFalse(solver.IsFollowing);
        }

        [Test]
        public void BeforeDwellElapses_MenuStaysPut()
        {
            var cfg = DefaultConfig();
            var start = MenuFollowSolver.ComputeIdealPose(Vector3.zero, Quaternion.identity, cfg);
            var solver = new MenuFollowSolver();
            solver.SnapHeight(0f);

            var end = Run(solver, start, cfg, Vector3.zero, Quaternion.Euler(0f, 40f, 0f), 0.30f);

            Assert.Less(Vector3.Distance(end.Position, start.Position), 1e-4f);
            Assert.IsFalse(solver.IsFollowing);
        }

        [Test]
        public void SustainedTurn_EngagesThenSettlesNearGaze_AndRelaxes()
        {
            var cfg = DefaultConfig();
            var start = MenuFollowSolver.ComputeIdealPose(Vector3.zero, Quaternion.identity, cfg);
            var solver = new MenuFollowSolver();
            solver.SnapHeight(0f);

            var headRotation = Quaternion.Euler(0f, 40f, 0f);
            var end = Run(solver, start, cfg, Vector3.zero, headRotation, 4f);

            // The menu moved a long way from where it started...
            Assert.Greater(Vector3.Distance(end.Position, start.Position), 0.1f);

            // ...to the inner settle ring: near the new gaze direction, but not chased to dead
            // centre and not glued to the cone edge (12 deg).
            float offGaze = Vector3.Angle(headRotation * Vector3.forward, end.Position);
            Assert.That(offGaze, Is.InRange(1f, 8f));

            // Follow distance re-imposed.
            Assert.That(Vector3.Distance(end.Position, Vector3.zero), Is.EqualTo(0.5f).Within(0.03f));

            // Panel faces the user and stays upright.
            Vector3 flatToMenu = end.Position;
            flatToMenu.y = 0f;
            float facingError = Quaternion.Angle(end.Rotation,
                Quaternion.LookRotation(flatToMenu.normalized, Vector3.up));
            Assert.Less(facingError, 2f);

            // Once it reaches the settle ring the follow relaxes.
            Assert.IsFalse(solver.IsFollowing);
        }

        [Test]
        public void YawOnly_LookDownAndTurn_LocksHeight_ButFollowsYaw()
        {
            var cfg = DefaultConfig();
            cfg.YawOnly = true;
            var solver = new MenuFollowSolver();
            solver.SnapHeight(0f);

            var start = MenuFollowSolver.ComputeIdealPose(Vector3.zero, Quaternion.identity, cfg, 0f);
            Assert.That(start.Position.y, Is.EqualTo(0f).Within(1e-4f));

            // Look down 45 deg and turn right 30 deg, held.
            var headRotation = Quaternion.Euler(-45f, 30f, 0f);
            var end = Run(solver, start, cfg, Vector3.zero, headRotation, 4f);

            // Height held despite the steep pitch.
            Assert.That(end.Position.y, Is.EqualTo(0f).Within(1e-3f));

            // Horizontal bearing followed the yaw, resting on the settle ring near the gaze azimuth.
            Vector3 flatGaze = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up).normalized;
            Vector3 flatToMenu = Vector3.ProjectOnPlane(end.Position, Vector3.up).normalized;
            Assert.That(Vector3.Angle(flatGaze, flatToMenu), Is.InRange(1f, 8f));
            Assert.IsFalse(solver.IsFollowing);
        }

        [Test]
        public void TurnAwayThenBack_MenuReturnsTowardGaze_AndRelaxes()
        {
            var cfg = DefaultConfig();
            var start = MenuFollowSolver.ComputeIdealPose(Vector3.zero, Quaternion.identity, cfg);
            var solver = new MenuFollowSolver();
            solver.SnapHeight(0f);

            var end = Run(solver, start, cfg,
                t => (Vector3.zero, Quaternion.Euler(0f, t < 3f ? 40f : 0f, 0f)),
                6f);

            // Head is forward again; the menu should have come back near centre and stopped following.
            Assert.Less(Vector3.Angle(Vector3.forward, end.Position), 8f);
            Assert.IsFalse(solver.IsFollowing);
        }

        [Test]
        public void ComputeIdealPose_PlacesMenuInFront_FacingUser_Upright()
        {
            var cfg = DefaultConfig();

            var pose = MenuFollowSolver.ComputeIdealPose(Vector3.zero, Quaternion.identity, cfg);
            Assert.Less(Vector3.Distance(pose.Position, new Vector3(0f, 0f, 0.5f)), 1e-4f);
            Assert.Less(Quaternion.Angle(pose.Rotation, Quaternion.identity), 0.1f);

            var headPosition = new Vector3(1f, 1.6f, 2f);
            var headRotation = Quaternion.Euler(0f, 90f, 0f);
            var pose2 = MenuFollowSolver.ComputeIdealPose(headPosition, headRotation, cfg);

            Assert.That(Vector3.Distance(pose2.Position, headPosition), Is.EqualTo(0.5f).Within(1e-4f));
            Vector3 euler = pose2.Rotation.eulerAngles;
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(euler.x, 0f)), 0.1f, "menu should not pitch");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(euler.z, 0f)), 0.1f, "menu should not roll");
        }
    }
}
