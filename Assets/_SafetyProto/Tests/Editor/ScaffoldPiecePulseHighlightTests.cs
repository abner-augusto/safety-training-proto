using NUnit.Framework;
using SafetyProto.Runtime.Scaffolding;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers the view-cone rule that decides when a scaffold piece counts as seen.
    /// </summary>
    public class ScaffoldPiecePulseHighlightTests
    {
        private const float HalfAngle = 35f;
        private const float MaxDistance = 5f;

        private static bool InView(Vector3 forward, Vector3 target) =>
            ScaffoldPiecePulseHighlight.IsInView(Vector3.zero, forward, target, HalfAngle, MaxDistance);

        [Test]
        public void StraightAheadWithinRangeIsInView()
        {
            Assert.IsTrue(InView(Vector3.forward, new Vector3(0f, 0f, 3f)));
        }

        [Test]
        public void InsideTheConeEdgeIsInView()
        {
            var target = Quaternion.Euler(0f, HalfAngle - 1f, 0f) * Vector3.forward * 2f;
            Assert.IsTrue(InView(Vector3.forward, target));
        }

        [Test]
        public void OutsideTheConeIsNotInView()
        {
            var target = Quaternion.Euler(0f, HalfAngle + 1f, 0f) * Vector3.forward * 2f;
            Assert.IsFalse(InView(Vector3.forward, target));
        }

        [Test]
        public void BehindTheHeadIsNotInView()
        {
            Assert.IsFalse(InView(Vector3.forward, new Vector3(0f, 0f, -2f)));
        }

        [Test]
        public void BeyondMaxDistanceIsNotInView()
        {
            Assert.IsFalse(InView(Vector3.forward, new Vector3(0f, 0f, MaxDistance + 0.1f)));
        }

        [Test]
        public void TargetAtTheHeadCountsAsInView()
        {
            Assert.IsTrue(InView(Vector3.forward, Vector3.zero));
        }
    }
}
