using NUnit.Framework;
using SafetyProto.Domain.Feedback;

namespace SafetyProto.Tests.Editor
{
    /// <summary>The three install states sound different and repeat while the piece stays there.</summary>
    public class PlacementCuePolicyTests
    {
        private PlacementCuePolicy _policy = null!;

        [SetUp]
        public void Setup() => _policy = new PlacementCuePolicy(nearMissRepeatSeconds: 0.6f, validRepeatSeconds: 1f);

        [Test]
        public void AwayFromTheSocket_IsSilent()
        {
            for (float t = 0f; t < 3f; t += 0.1f)
                Assert.AreEqual(PlacementCue.None, _policy.Tick(t, held: true, PlacementPose.Away));
        }

        [Test]
        public void EachStateHasItsOwnCueOnEntry()
        {
            Assert.AreEqual(PlacementCue.NearMiss, _policy.Tick(0f, true, PlacementPose.WrongAngle));
            Assert.AreEqual(PlacementCue.Valid, _policy.Tick(0.1f, true, PlacementPose.Valid));
        }

        [Test]
        public void StayingInAState_RepeatsAtItsInterval()
        {
            Assert.AreEqual(PlacementCue.Valid, _policy.Tick(0f, true, PlacementPose.Valid));
            Assert.AreEqual(PlacementCue.None, _policy.Tick(0.9f, true, PlacementPose.Valid));
            Assert.AreEqual(PlacementCue.Valid, _policy.Tick(1.0f, true, PlacementPose.Valid));

            Assert.AreEqual(PlacementCue.NearMiss, _policy.Tick(1.1f, true, PlacementPose.WrongAngle));
            Assert.AreEqual(PlacementCue.None, _policy.Tick(1.6f, true, PlacementPose.WrongAngle));
            Assert.AreEqual(PlacementCue.NearMiss, _policy.Tick(1.7f, true, PlacementPose.WrongAngle));
        }

        [Test]
        public void ZeroRepeat_PlaysOncePerEntry()
        {
            _policy = new PlacementCuePolicy(0f, 0f);

            Assert.AreEqual(PlacementCue.Valid, _policy.Tick(0f, true, PlacementPose.Valid));
            Assert.AreEqual(PlacementCue.None, _policy.Tick(5f, true, PlacementPose.Valid));
            _policy.Tick(5.1f, true, PlacementPose.Away);
            Assert.AreEqual(PlacementCue.Valid, _policy.Tick(5.2f, true, PlacementPose.Valid));
        }

        [Test]
        public void LettingGo_IsSilentAndTheNextHoldStartsFresh()
        {
            _policy.Tick(0f, true, PlacementPose.Valid);

            Assert.AreEqual(PlacementCue.None, _policy.Tick(0.1f, held: false, PlacementPose.Valid));
            Assert.AreEqual(PlacementCue.Valid, _policy.Tick(0.2f, held: true, PlacementPose.Valid));
        }
    }
}
