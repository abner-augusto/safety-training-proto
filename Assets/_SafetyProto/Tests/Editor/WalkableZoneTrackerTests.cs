using NUnit.Framework;
using SafetyProto.Runtime;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class WalkableZoneTrackerTests
    {
        private static WalkableZoneTracker Plank() =>
            new WalkableZoneTracker(new Vector2(2.4f, 0.7f), exitDelay: 0.3f, reentryDepth: 0.15f);

        [Test]
        public void BriefStepOutside_ShorterThanExitDelay_DoesNotFire()
        {
            var tracker = Plank();

            Assert.AreEqual(WalkableZoneTracker.Transition.None, tracker.Step(new Vector2(0f, 0.5f), 0.2f));
            Assert.AreEqual(WalkableZoneTracker.Transition.None, tracker.Step(Vector2.zero, 0.2f));
            Assert.AreEqual(WalkableZoneTracker.Transition.None, tracker.Step(new Vector2(0f, 0.5f), 0.2f));

            Assert.IsFalse(tracker.IsOutside);
        }

        [Test]
        public void StayingOutside_FiresLeftOnce_ThenReturnedWithDuration()
        {
            var tracker = Plank();
            var past = new Vector2(0.3f, 0.6f);

            Assert.AreEqual(WalkableZoneTracker.Transition.None, tracker.Step(past, 0.2f));
            Assert.AreEqual(WalkableZoneTracker.Transition.Left, tracker.Step(past, 0.2f));
            Assert.AreEqual(WalkableZoneTracker.Transition.None, tracker.Step(past, 1.0f));

            Assert.AreEqual(WalkableZoneTracker.Transition.Returned, tracker.Step(Vector2.zero, 0.1f));
            Assert.AreEqual(1.5f, tracker.OutsideSeconds, 0.0001f);
            Assert.IsFalse(tracker.IsOutside);
        }

        [Test]
        public void StandingOnTheEdge_DoesNotCountAsReturned()
        {
            var tracker = Plank();
            tracker.Step(new Vector2(0f, 0.6f), 0.5f);
            Assert.IsTrue(tracker.IsOutside);

            // Inside the 0.35 m half-depth but not 0.15 m past the edge.
            Assert.AreEqual(WalkableZoneTracker.Transition.None, tracker.Step(new Vector2(0f, 0.3f), 0.1f));
            Assert.IsTrue(tracker.IsOutside);

            Assert.AreEqual(WalkableZoneTracker.Transition.Returned, tracker.Step(new Vector2(0f, 0.19f), 0.1f));
        }

        [Test]
        public void NearestSafePoint_IsOnTheCenterLine_ClampedAlongTheWidth()
        {
            var tracker = Plank();

            Assert.AreEqual(new Vector2(0.4f, 0f), tracker.NearestSafePoint(new Vector2(0.4f, -0.9f)));
            Assert.AreEqual(1.05f, tracker.NearestSafePoint(new Vector2(3f, 0.2f)).x, 0.0001f);
            Assert.AreEqual(-1.05f, tracker.NearestSafePoint(new Vector2(-3f, 0f)).x, 0.0001f);
        }

        [Test]
        public void Reset_ForgetsTheExitWithoutReportingAReturn()
        {
            var tracker = Plank();
            tracker.Step(new Vector2(0f, 0.6f), 0.5f);
            Assert.IsTrue(tracker.IsOutside);

            tracker.Reset();

            Assert.IsFalse(tracker.IsOutside);
            Assert.AreEqual(WalkableZoneTracker.Transition.None, tracker.Step(Vector2.zero, 0.1f));
        }
    }
}
