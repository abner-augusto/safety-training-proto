using NUnit.Framework;
using SafetyProto.Runtime.Simulation;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class SimulatedPlayerMoverTests
    {
        private GameObject _rig = null!;
        private Transform _head = null!;

        [SetUp]
        public void Setup()
        {
            _rig = new GameObject("rig");
            _head = new GameObject("head").transform;
            _head.SetParent(_rig.transform, false);
            _head.localPosition = new Vector3(0.1f, 0f, 0.2f);   // tracked offset inside the rig
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_rig);

        [TestCase(null, SimulatedPosture.Standing)]
        [TestCase("", SimulatedPosture.Standing)]
        [TestCase("standing", SimulatedPosture.Standing)]
        [TestCase("Crouched", SimulatedPosture.Crouched)]
        [TestCase("agachado", SimulatedPosture.Crouched)]
        [TestCase("em_pe", SimulatedPosture.Standing)]
        public void TryParsePosture_AcceptsKnownNames(string? value, SimulatedPosture expected)
        {
            Assert.IsTrue(SimulatedPlayerMover.TryParsePosture(value, out var posture));
            Assert.AreEqual(expected, posture);
        }

        [Test]
        public void TryParsePosture_RejectsUnknownName()
        {
            Assert.IsFalse(SimulatedPlayerMover.TryParsePosture("sitting", out _));
        }

        [Test]
        public void PlaceRig_PutsTheEyesAtStandPointPlusPostureHeight()
        {
            var stand = new Vector3(-1f, 21.728f, 12.3f);

            SimulatedPlayerMover.PlaceRig(_rig.transform, _head, stand, stand, SimulatedPosture.Crouched);

            var expected = stand + Vector3.up * SimulatedPlayerMover.CrouchedEyeHeight;
            Assert.That(Vector3.Distance(expected, _head.position), Is.LessThan(1e-4f), _head.position.ToString("F4"));
        }

        [Test]
        public void PlaceRig_TurnsTheHeadTowardsTheLookAtPoint()
        {
            var stand = new Vector3(0f, 0f, 0f);
            var lookAt = new Vector3(3f, 1f, 0f);   // due +X

            SimulatedPlayerMover.PlaceRig(_rig.transform, _head, stand, lookAt, SimulatedPosture.Standing);

            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            Assert.That(Vector3.Angle(forward, Vector3.right), Is.LessThan(0.01f));
            Assert.AreEqual(SimulatedPlayerMover.StandingEyeHeight, _head.position.y, 1e-4f);
        }

        [Test]
        public void PlaceRig_KeepsTheHeadingWhenThereIsNothingToFace()
        {
            _rig.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
            var stand = new Vector3(5f, 0f, 5f);

            SimulatedPlayerMover.PlaceRig(_rig.transform, _head, stand, stand, SimulatedPosture.Standing);

            Assert.AreEqual(40f, _rig.transform.eulerAngles.y, 0.01f);
        }
    }
}
