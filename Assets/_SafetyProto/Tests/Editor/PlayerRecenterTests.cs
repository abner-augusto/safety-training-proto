using NUnit.Framework;
using SafetyProto.Runtime;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class PlayerRecenterTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void Solve_HeadAtRigOrigin_TargetAtOrigin_SameYaw_RigUnchanged()
        {
            var solution = PlayerRecenter.Solve(
                rigPosition: Vector3.zero,
                headPosition: Vector3.zero,
                headYaw: 0f,
                targetPos: Vector3.zero,
                targetYaw: 0f);

            AssertApprox(Vector3.zero, solution.RigPosition);
            Assert.AreEqual(0f, solution.RigYawDelta, Epsilon);
        }

        [Test]
        public void Solve_RoomScaleOffsetCancelled_HeadLandsOnTarget_NotAppliedToRig()
        {            var rigPosition = Vector3.zero;
            var headPosition = new Vector3(0f, 0f, 0.5f);
            var target = new Vector3(10f, 0f, 0f);

            var solution = PlayerRecenter.Solve(rigPosition, headPosition, headYaw: 0f, target, targetYaw: 0f);
            Vector3 headOffsetFromRig = headPosition - rigPosition;
            Vector3 headFinal = solution.RigPosition + headOffsetFromRig;
            AssertApprox(target, headFinal);
            Assert.AreNotEqual(target, solution.RigPosition);
        }

        [Test]
        public void Solve_YawDelta_IsShortestSignedPath_HeadStillLandsOnTarget()
        {
            var rigPosition = new Vector3(0f, 0f, -1f);
            var headPosition = Vector3.zero;
            const float headYaw = 0f;
            const float targetYaw = 90f;
            var target = new Vector3(5f, 0f, 5f);

            var solution = PlayerRecenter.Solve(rigPosition, headPosition, headYaw, target, targetYaw);

            Assert.AreEqual(Mathf.DeltaAngle(headYaw, targetYaw), solution.RigYawDelta, Epsilon);

            Vector3 rigAfterRotation = RotateAround(rigPosition, headPosition, solution.RigYawDelta);
            Vector3 headOffsetFromRig = headPosition - rigAfterRotation;
            Vector3 headFinal = solution.RigPosition + headOffsetFromRig;
            AssertApprox(target, headFinal);
        }

        [Test]
        public void Solve_YawWrapNear180_UsesShortestPath()
        {            var solution = PlayerRecenter.Solve(Vector3.zero, Vector3.zero, headYaw: -170f, Vector3.zero, targetYaw: 170f);
            Assert.AreEqual(-20f, solution.RigYawDelta, Epsilon);
        }

        [Test]
        public void Solve_TargetYDiffers_RigYMatchesTarget_HeadYNotForced()
        {
            var rigPosition = new Vector3(0f, 0f, 0f);
            var headPosition = new Vector3(0f, 1.7f, 0f);
            var target = new Vector3(3f, 2f, 3f);

            var solution = PlayerRecenter.Solve(rigPosition, headPosition, headYaw: 0f, target, targetYaw: 0f);
            Assert.AreEqual(target.y, solution.RigPosition.y, Epsilon);
        }

        [Test]
        public void Recenter_StudentOffCenterFacingAway_LandsOnAnchorFacingAnchorYaw()
        {            var rig = new GameObject("Rig").transform;
            var head = new GameObject("CenterEyeAnchor").transform;
            head.SetParent(rig);
            head.localPosition = new Vector3(1f, 1.7f, 0.3f);
            rig.rotation = Quaternion.Euler(0f, -90f, 0f);

            var anchor = new GameObject("Anchor").transform;
            anchor.position = new Vector3(20f, 0f, -8f);
            anchor.rotation = Quaternion.Euler(0f, 45f, 0f);

            try
            {
                PlayerRecenter.Recenter(rig, head, anchor);

                Assert.AreEqual(anchor.position.x, head.position.x, 0.001f, "head x");
                Assert.AreEqual(anchor.position.z, head.position.z, 0.001f, "head z");
                Assert.AreEqual(anchor.position.y, rig.position.y, Epsilon, "rig floor y");
                Assert.AreEqual(0f, Mathf.DeltaAngle(anchor.eulerAngles.y, head.eulerAngles.y), 0.01f, "head yaw");
            }
            finally
            {
                Object.DestroyImmediate(rig.gameObject);
                Object.DestroyImmediate(anchor.gameObject);
            }
        }

        [TestCase(0f, 0f)]
        [TestCase(40f, 0f)]
        [TestCase(89.5f, 0f)]
        [TestCase(-89.5f, 0f)]
        [TestCase(80f, 135f)]
        public void HeadingYaw_IgnoresPitch_EvenLookingStraightDown(float pitch, float yaw)
        {
            float heading = PlayerRecenter.HeadingYaw(Quaternion.Euler(pitch, yaw, 0f));

            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, heading), 0.01f);
        }

        [Test]
        public void RoomCenter_SurvivesRigMoves_AndLandsOnAnchorWhereverTheHeadIs()
        {
            var rig = new GameObject("Rig").transform;
            var head = new GameObject("CenterEyeAnchor").transform;
            head.SetParent(rig);
            var anchor = new GameObject("Anchor").transform;

            try
            {
                // Participant on the floor mark, facing the room's front, looking down at the mark.
                rig.SetPositionAndRotation(new Vector3(2f, 0f, 1f), Quaternion.Euler(0f, 30f, 0f));
                head.localPosition = new Vector3(0.4f, 1.7f, -0.2f);
                head.localRotation = Quaternion.Euler(60f, 15f, 0f);

                var calibration = new RoomCenterCalibration();
                calibration.Capture(rig, head.position, head.rotation, RoomCenterSource.Operator);
                Assert.IsTrue(calibration.TryGetWorld(rig, out var markBefore, out var frontBefore));
                Vector3 markInRoom = rig.InverseTransformPoint(markBefore);

                // Artificial locomotion moves the rig; the participant walks 1.2 m away and turns.
                rig.SetPositionAndRotation(new Vector3(-7f, 3f, 4f), Quaternion.Euler(0f, -100f, 0f));
                head.localPosition = new Vector3(-0.8f, 1.7f, 0.9f);
                head.localRotation = Quaternion.Euler(0f, 170f, 0f);

                anchor.SetPositionAndRotation(new Vector3(-1f, 21.7f, 12.5f), Quaternion.Euler(0f, 180f, 0f));
                Assert.IsTrue(calibration.TryGetWorld(rig, out var mark, out var front));
                PlayerRecenter.Recenter(rig, mark, front, anchor.position, anchor.eulerAngles.y);

                Assert.IsTrue(calibration.TryGetWorld(rig, out var markAfter, out var frontAfter));
                Assert.AreEqual(anchor.position.x, markAfter.x, 0.001f, "mark x");
                Assert.AreEqual(anchor.position.z, markAfter.z, 0.001f, "mark z");
                Assert.AreEqual(anchor.position.y, rig.position.y, Epsilon, "rig floor y");
                Assert.AreEqual(0f, Mathf.DeltaAngle(180f, frontAfter), 0.01f, "room front");
                AssertApprox(markInRoom, rig.InverseTransformPoint(markAfter), 0.001f);
                Assert.AreEqual(0f, Mathf.DeltaAngle(frontBefore - 30f, frontAfter - rig.eulerAngles.y), 0.01f, "front in room");
            }
            finally
            {
                Object.DestroyImmediate(rig.gameObject);
                Object.DestroyImmediate(anchor.gameObject);
            }
        }

        [Test]
        public void LandingReference_KeepsAllowedOffset_ClampsTheRest()
        {
            var center = new Vector3(1f, 0f, 2f);
            const float roomYaw = 90f; // room forward = world +X, room right = world -Z
            // Body 1.2 m right and 0.9 m forward of the center.
            var body = center + new Vector3(0.9f, 1.4f, -1.2f);

            Vector3 reference = RoomCenterCalibration.LandingReference(center, roomYaw, body,
                offset => new Vector2(Mathf.Clamp(offset.x, -1f, 0.8f), Mathf.Clamp(offset.y, -0.25f, 0.25f)));

            // Placing the reference on the anchor puts the body at (body - reference) from it.
            Vector3 landed = Quaternion.Euler(0f, -roomYaw, 0f) * (body - reference);
            Assert.AreEqual(0.8f, landed.x, 0.001f, "lateral clamped to 0.8 m right");
            Assert.AreEqual(0.25f, landed.z, 0.001f, "depth clamped to 0.25 m forward");
            Assert.AreEqual(center.y, reference.y, Epsilon, "reference stays on the center's height");

            Vector3 unclamped = RoomCenterCalibration.LandingReference(center, roomYaw, body, offset => offset);
            AssertApprox(center, unclamped);
        }

        [Test]
        public void RoomCenter_IsUnderTheNeck_NotTheEyes()
        {
            var rig = new GameObject("Rig").transform;
            try
            {
                var calibration = new RoomCenterCalibration();
                calibration.Capture(rig, new Vector3(0f, 1.7f, 0f), Quaternion.identity, RoomCenterSource.Operator);
                Assert.IsTrue(calibration.TryGetWorld(rig, out var upright, out _));

                calibration.Capture(rig, new Vector3(0f, 1.6f, 0.1f), Quaternion.Euler(70f, 0f, 0f), RoomCenterSource.Operator);
                Assert.IsTrue(calibration.TryGetWorld(rig, out var lookingDown, out _));

                Assert.Less(upright.z, -0.07f, "neck sits behind the eyes");
                Assert.AreEqual(upright.x, lookingDown.x, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(rig.gameObject);
            }
        }

        [Test]
        public void RoomCenter_ClearedOrNeverCaptured_ReportsNoValue()
        {
            var rig = new GameObject("Rig").transform;
            try
            {
                var calibration = new RoomCenterCalibration();
                Assert.IsFalse(calibration.TryGetWorld(rig, out _, out _));

                calibration.Capture(rig, Vector3.up, Quaternion.identity, RoomCenterSource.SessionStart);
                Assert.AreEqual(RoomCenterSource.SessionStart, calibration.Source);

                calibration.Clear();
                Assert.IsFalse(calibration.HasValue);
                Assert.IsFalse(calibration.TryGetWorld(rig, out _, out _));
            }
            finally
            {
                Object.DestroyImmediate(rig.gameObject);
            }
        }

        private static Vector3 RotateAround(Vector3 point, Vector3 pivot, float yawDelta)
        {
            Vector3 relative = point - pivot;
            return pivot + Quaternion.AngleAxis(yawDelta, Vector3.up) * relative;
        }

        private static void AssertApprox(Vector3 expected, Vector3 actual, float epsilon = Epsilon)
        {
            Assert.AreEqual(expected.x, actual.x, epsilon, "x");
            Assert.AreEqual(expected.y, actual.y, epsilon, "y");
            Assert.AreEqual(expected.z, actual.z, epsilon, "z");
        }
    }
}
