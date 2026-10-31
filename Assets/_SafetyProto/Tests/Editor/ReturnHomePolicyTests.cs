using NUnit.Framework;
using SafetyProto.Domain.Feedback;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// The return-home decision for loose objects, driven frame by frame the way ReturnObjectHome
    /// feeds it. Values mirror the PPE prefabs: 2 s delay, 0.3 m from home, 0.3 m drop.
    /// </summary>
    public class ReturnHomePolicyTests
    {
        private const float Delay = 2f;
        private const float Frame = 0.1f;

        private ReturnHomePolicy _policy = null!;

        [SetUp]
        public void Setup() => _policy = new ReturnHomePolicy(Delay, maxHomeDistance: 0.3f, maxDropBelowHome: 0.3f);

        private static ReturnHomeObservation AtHome(bool held = false, bool secured = false)
            => new ReturnHomeObservation(held, secured, 0f, 0f, false);

        private static ReturnHomeObservation Away(float distance, bool held = false, bool secured = false, float drop = 0f)
            => new ReturnHomeObservation(held, secured, distance, drop, false);

        /// <summary>Ticks <paramref name="seconds"/> worth of frames; returns the trigger that fired, if any.</summary>
        private ReturnHomeTrigger Run(float seconds, ReturnHomeObservation observation)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Frame)
                if (_policy.Tick(Frame, observation, out var fired)) return fired;
            return ReturnHomeTrigger.None;
        }

        [Test]
        public void GrabThenReleaseFarAway_ReturnsAfterTheDelay()
        {
            Run(0.5f, Away(1f, held: true));

            Assert.AreEqual(ReturnHomeTrigger.None, Run(Delay - 0.3f, Away(1f)), "too early");
            Assert.AreEqual(ReturnHomeTrigger.Release, Run(0.5f, Away(1f)));
        }

        [Test]
        public void GrabbedAgainDuringTheWait_DoesNotReturn()
        {
            Run(0.5f, Away(1f, held: true));
            Run(1f, Away(1f));
            Run(1f, Away(1f, held: true));

            Assert.AreEqual(ReturnHomeTrigger.None, Run(0.1f, Away(1f, held: true)));
            Assert.AreEqual(ReturnHomeTrigger.None, _policy.Pending);
        }

        [Test]
        public void GrabSnapUnsnap_ReturnsOnlyAfterTheHandLetsGoOffTheSlot()
        {
            Run(0.5f, Away(1f, held: true));
            Assert.AreEqual(ReturnHomeTrigger.None, Run(5f, Away(1f, secured: true)), "worn on the body");

            Run(0.5f, Away(1f, held: true, secured: true));
            Run(0.5f, Away(1f, held: true));
            Assert.AreEqual(ReturnHomeTrigger.Release, Run(Delay + 0.2f, Away(1f)));
        }

        [Test]
        public void PushedWithoutEverBeingHeld_ReturnsAfterTheDelay()
        {
            Run(1f, AtHome());

            Assert.AreEqual(ReturnHomeTrigger.Displaced, Run(Delay + 0.2f, Away(0.5f)));
        }

        [Test]
        public void PushedAfterAnEarlierGrabAndReturn_StillReturns()
        {
            Run(0.5f, Away(1f, held: true));
            Assert.AreEqual(ReturnHomeTrigger.Release, Run(Delay + 0.2f, Away(1f)));
            Run(1f, AtHome());

            Assert.AreEqual(ReturnHomeTrigger.Displaced, Run(Delay + 0.2f, Away(0.5f)));
        }

        [Test]
        public void TicksStopDuringTheWait_ThePendingReturnSurvives()
        {
            Run(1f, Away(0.5f));
            Assert.AreEqual(ReturnHomeTrigger.Displaced, _policy.Pending);

            // No ticks while the object is switched off; it fires once ticks resume.
            Assert.AreEqual(ReturnHomeTrigger.Displaced, Run(Delay, Away(0.5f)));
        }

        [Test]
        public void SlightNudgeWithinRange_StaysPut()
        {
            Assert.AreEqual(ReturnHomeTrigger.None, Run(10f, Away(0.2f)));
        }

        [Test]
        public void KnockedOutThenRollsBackIntoRange_CancelsTheReturn()
        {
            Run(1f, Away(0.5f));
            Run(0.1f, Away(0.1f));

            Assert.AreEqual(ReturnHomeTrigger.None, Run(Delay + 0.5f, Away(0.1f)));
        }

        [Test]
        public void FellOffTheTableStraightDown_CountsAsDisplaced()
        {
            Assert.AreEqual(ReturnHomeTrigger.Displaced, Run(Delay + 0.2f, Away(0.25f, drop: 0.8f)));
        }

        [Test]
        public void LeftTheZone_CountsAsDisplaced()
        {
            var outside = new ReturnHomeObservation(false, false, 0.1f, 0f, outsideZone: true);

            Assert.AreEqual(ReturnHomeTrigger.Displaced, Run(Delay + 0.2f, outside));
        }

        [Test]
        public void DistanceCheckOff_OnlyReleaseAndDropCount()
        {
            _policy = new ReturnHomePolicy(Delay, maxHomeDistance: 0f, maxDropBelowHome: 0f);

            Assert.AreEqual(ReturnHomeTrigger.None, Run(10f, Away(5f, drop: 2f)));
        }

        [Test]
        public void InstalledFarFromHome_NeverReturns()
        {
            Run(0.5f, Away(3f, held: true));

            Assert.AreEqual(ReturnHomeTrigger.None, Run(10f, Away(3f, secured: true)));
        }

        [Test]
        public void ZeroDelay_ReturnsOnTheReleaseFrame()
        {
            _policy = new ReturnHomePolicy(0f, 0.3f, 0.3f);
            _policy.Tick(Frame, Away(1f, held: true), out _);

            Assert.IsTrue(_policy.Tick(Frame, Away(1f), out var fired));
            Assert.AreEqual(ReturnHomeTrigger.Release, fired);
        }

        [Test]
        public void Request_ReturnsAfterTheDelayEvenWithinRange()
        {
            _policy.Request();

            Assert.AreEqual(ReturnHomeTrigger.None, Run(Delay - 0.3f, Away(0.1f)));
            Assert.AreEqual(ReturnHomeTrigger.Requested, Run(0.5f, Away(0.1f)));
        }
    }
}
