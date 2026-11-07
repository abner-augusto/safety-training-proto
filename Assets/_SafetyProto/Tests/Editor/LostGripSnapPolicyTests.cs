using NUnit.Framework;
using SafetyProto.Domain.Feedback;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// The helmet pressed to the head: the hand vanishes or jumps before it opens, and the item
    /// still counts as let go at the slot it was at.
    /// </summary>
    public class LostGripSnapPolicyTests
    {
        private LostGripSnapPolicy _policy = null!;

        [SetUp]
        public void Setup() => _policy = new LostGripSnapPolicy(graceSeconds: 0.4f, lostConfirmSeconds: 0.1f, jumpSpeed: 5f);

        [Test]
        public void TrackingLostAtTheSlot_FiresAfterTheConfirmTime()
        {
            Assert.AreEqual(LostGripCause.None, _policy.Observe(0f, nearSlot: true, handTracked: true, speed: 0.5f));
            Assert.AreEqual(LostGripCause.None, _policy.Observe(0.05f, nearSlot: true, handTracked: false, speed: 0f));
            Assert.AreEqual(LostGripCause.TrackingLost, _policy.Observe(0.15f, nearSlot: true, handTracked: false, speed: 0f));
        }

        [Test]
        public void OneFrameDropout_DoesNotFire()
        {
            _policy.Observe(0f, true, true, 0f);
            _policy.Observe(0.02f, true, false, 0f);
            Assert.AreEqual(LostGripCause.None, _policy.Observe(0.04f, true, true, 0f));
            Assert.AreEqual(LostGripCause.None, _policy.Observe(0.2f, true, true, 0f));
        }

        [Test]
        public void TrackingLostAwayFromAnySlot_DoesNotFire()
        {
            _policy.Observe(0f, nearSlot: false, handTracked: true, speed: 0f);
            Assert.AreEqual(LostGripCause.None, _policy.Observe(0.1f, false, false, 0f));
            Assert.AreEqual(LostGripCause.None, _policy.Observe(1f, false, false, 0f));
        }

        [Test]
        public void TrackingLostJustAfterLeavingTheSlot_StillFires()
        {
            _policy.Observe(0f, nearSlot: true, handTracked: true, speed: 0.5f);
            _policy.Observe(0.1f, nearSlot: false, handTracked: false, speed: 1f);
            Assert.AreEqual(LostGripCause.TrackingLost, _policy.Observe(0.25f, false, false, 0f));
        }

        [Test]
        public void TrackingLostAfterTheGrace_DoesNotFire()
        {
            _policy.Observe(0f, nearSlot: true, handTracked: true, speed: 0f);
            _policy.Observe(0.5f, nearSlot: false, handTracked: false, speed: 0f);
            Assert.AreEqual(LostGripCause.None, _policy.Observe(0.7f, false, false, 0f));
        }

        [Test]
        public void FastExitFromTheSlot_IsAJump()
        {
            _policy.Observe(0f, nearSlot: true, handTracked: true, speed: 0.4f);
            Assert.AreEqual(LostGripCause.Jump, _policy.Observe(0.014f, nearSlot: false, handTracked: true, speed: 12f));
        }

        [Test]
        public void HandMovingAwayAtHandSpeed_IsNotAJump()
        {
            _policy.Observe(0f, nearSlot: true, handTracked: true, speed: 0.4f);
            Assert.AreEqual(LostGripCause.None, _policy.Observe(0.014f, nearSlot: false, handTracked: true, speed: 2.5f));
            Assert.AreEqual(LostGripCause.None, _policy.Observe(1f, nearSlot: false, handTracked: true, speed: 12f));
        }

        [Test]
        public void FastMotionInsideTheSlot_IsNotAJump()
        {
            Assert.AreEqual(LostGripCause.None, _policy.Observe(0f, nearSlot: true, handTracked: true, speed: 12f));
        }

        [Test]
        public void FiresOncePerHold_UntilReset()
        {
            _policy.Observe(0f, true, true, 0f);
            Assert.AreEqual(LostGripCause.Jump, _policy.Observe(0.01f, false, true, 12f));
            _policy.Observe(0.02f, true, true, 0f);
            Assert.AreEqual(LostGripCause.None, _policy.Observe(0.03f, false, true, 12f));
            Assert.IsFalse(_policy.CountsAtSlotOnRelease(0.04f, handTracked: false));

            _policy.Reset();
            _policy.Observe(1f, true, true, 0f);
            Assert.AreEqual(LostGripCause.Jump, _policy.Observe(1.01f, false, true, 12f));
        }

        [Test]
        public void ReleaseAwayFromTheSlot_CountsOnlyWhenUntrackedAndRecent()
        {
            _policy.Observe(0f, nearSlot: true, handTracked: true, speed: 0f);
            Assert.IsFalse(_policy.CountsAtSlotOnRelease(0.1f, handTracked: true), "a tracked hand opening away from the slot is a normal drop");
            Assert.IsTrue(_policy.CountsAtSlotOnRelease(0.1f, handTracked: false));
            Assert.IsFalse(_policy.CountsAtSlotOnRelease(0.6f, handTracked: false));
        }

        [Test]
        public void ResetForgetsTheSlot()
        {
            _policy.Observe(0f, true, true, 0f);
            _policy.Reset();
            Assert.IsFalse(_policy.RecentlyNear(0.1f));
        }
    }
}
