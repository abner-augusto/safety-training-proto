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
