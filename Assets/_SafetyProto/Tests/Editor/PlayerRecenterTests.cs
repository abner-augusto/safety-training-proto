// Assets/_SafetyProto/Tests/Editor/PlayerRecenterTests.cs
//
// Unity EditMode only (uses Vector3/Mathf/GameObject) — not linked into the headless
// Tools/SafetyProto.Tests csproj. Covers R2 (yaw bound to instantaneous head rotation) via the
// pure PlayerRecenter.Solve function, plus one end-to-end Recenter() case reproducing the
// regression this plan exists to fix (see plans/026-evaluator-recenter-playspace.md, Step 1).
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
        {
            // Head sits 0.5m off the rig origin (room-scale offset); target is 10m away; same yaw.
            var rigPosition = Vector3.zero;
            var headPosition = new Vector3(0f, 0f, 0.5f);
            var target = new Vector3(10f, 0f, 0f);

            var solution = PlayerRecenter.Solve(rigPosition, headPosition, headYaw: 0f, target, targetYaw: 0f);

            // Yaw delta is zero, so the head's offset from the rig does not change; the head must
            // land exactly on target once the rig is moved to solution.RigPosition.
            Vector3 headOffsetFromRig = headPosition - rigPosition;
            Vector3 headFinal = solution.RigPosition + headOffsetFromRig;
            AssertApprox(target, headFinal);

            // The room-scale offset must be cancelled, not handed straight to the rig.
            Assert.AreNotEqual(target, solution.RigPosition);
        }

        [Test]
        public void Solve_YawDelta_IsShortestSignedPath_HeadStillLandsOnTarget()
        {
            var rigPosition = new Vector3(0f, 0f, -1f); // 1m behind the head
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
        {
            // DeltaAngle(-170, 170) is -20 (the short way around), not 340.
            var solution = PlayerRecenter.Solve(Vector3.zero, Vector3.zero, headYaw: -170f, Vector3.zero, targetYaw: 170f);
            Assert.AreEqual(-20f, solution.RigYawDelta, Epsilon);
        }

        [Test]
        public void Solve_TargetYDiffers_RigYMatchesTarget_HeadYNotForced()
        {
            var rigPosition = new Vector3(0f, 0f, 0f);
            var headPosition = new Vector3(0f, 1.7f, 0f); // head 1.7m above the rig floor
            var target = new Vector3(3f, 2f, 3f);

            var solution = PlayerRecenter.Solve(rigPosition, headPosition, headYaw: 0f, target, targetYaw: 0f);

            // Rig floor lands exactly at the target's Y (feet on deck)...
            Assert.AreEqual(target.y, solution.RigPosition.y, Epsilon);

            // ...but the head is not forced to target.y — it keeps its height above the rig floor.
            float headHeightAboveRig = headPosition.y - rigPosition.y;
            float expectedHeadY = solution.RigPosition.y + headHeightAboveRig;
            Assert.AreNotEqual(target.y, expectedHeadY);
        }

        [Test]
        public void Recenter_StudentOffCenterFacingAway_LandsOnAnchorFacingAnchorYaw()
        {
            // R2 regression case: a student standing off-center and facing well away from the
            // anchor's yaw must still land exactly on the anchor, facing the anchor's yaw. Before
            // this fix, yaw was bound to whatever direction the head happened to face at the
            // instant of the call.
            var rig = new GameObject("Rig").transform;
            var head = new GameObject("CenterEyeAnchor").transform;
            head.SetParent(rig);
            head.localPosition = new Vector3(1f, 1.7f, 0.3f); // off-center room-scale offset
            rig.rotation = Quaternion.Euler(0f, -90f, 0f);    // facing away from the anchor's yaw

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
