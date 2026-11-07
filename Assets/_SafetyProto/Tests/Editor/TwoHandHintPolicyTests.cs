using NUnit.Framework;
using SafetyProto.Domain.Feedback;

namespace SafetyProto.Tests.Editor
{
    /// <summary>The toeboard warning: one hand for about a second shows it, the second hand removes it.</summary>
    public class TwoHandHintPolicyTests
    {
        private TwoHandHintPolicy _policy = null!;

        [SetUp]
        public void Setup() => _policy = new TwoHandHintPolicy(delaySeconds: 0.8f, cueCooldownSeconds: 8f);

        [Test]
        public void OneHand_ShowsWithSoundAfterTheDelay()
        {
            Assert.IsFalse(_policy.Tick(0f, 1).Show);
            Assert.IsFalse(_policy.Tick(0.7f, 1).Show);

            var step = _policy.Tick(0.8f, 1);
            Assert.IsTrue(step.Show);
            Assert.IsTrue(step.PlayCue);
            Assert.IsTrue(_policy.Showing);
        }

        [Test]
        public void ShowingStaysQuietWhileTheOneHandKeepsHolding()
        {
            _policy.Tick(0f, 1);
            _policy.Tick(1f, 1);

            var later = _policy.Tick(5f, 1);
            Assert.IsFalse(later.Show);
            Assert.IsFalse(later.PlayCue);
            Assert.IsTrue(_policy.Showing);
        }

        [Test]
        public void SecondHand_HidesIt()
        {
            _policy.Tick(0f, 1);
            _policy.Tick(1f, 1);

            var step = _policy.Tick(1.5f, 2);
            Assert.IsTrue(step.Hide);
            Assert.IsFalse(_policy.Showing);
        }

        [Test]
        public void ReachingWithTheSecondHandInTime_NeverWarns()
        {
            _policy.Tick(0f, 1);
            Assert.IsFalse(_policy.Tick(0.5f, 2).Show);
            Assert.IsFalse(_policy.Tick(3f, 2).Show);
            Assert.IsFalse(_policy.Showing);
        }

        [Test]
        public void LettingGo_HidesIt()
        {
            _policy.Tick(0f, 1);
            _policy.Tick(1f, 1);

            Assert.IsTrue(_policy.Tick(1.2f, 0).Hide);
        }

        [Test]
        public void RegrabbingSoon_ShowsAgainWithoutSound()
        {
            _policy.Tick(0f, 1);
            _policy.Tick(1f, 1);
            _policy.Tick(2f, 0);
            _policy.Tick(3f, 1);

            var again = _policy.Tick(4f, 1);
            Assert.IsTrue(again.Show);
            Assert.IsFalse(again.PlayCue, "within the cooldown");

            _policy.Tick(5f, 0);
            _policy.Tick(10f, 1);
            Assert.IsTrue(_policy.Tick(11f, 1).PlayCue, "after the cooldown");
        }
    }
}
