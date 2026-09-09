using NUnit.Framework;
using SafetyProto.Domain.Safety;

namespace SafetyProto.Tests.Editor
{
    public class RefusedAttemptTrackerTests
    {
        private const string MyAction = "install_guardrail";
        private const string MySource = "GuardRailPiece";
        private const string Code = "PREREQUISITE_PENDING";

        [Test]
        public void MatchingActionAndSource_IsMine()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: false, now: 0f, fallbackSeconds: 0f);

            Assert.IsTrue(tracker.TryTakeRevert(0f));
        }

        [Test]
        public void DifferentAction_IsNotMine()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe("install_toeboard", MySource, Code, MyAction, MySource, waitForPopup: false, now: 0f, fallbackSeconds: 0f);

            Assert.IsFalse(tracker.TryTakeRevert(0f));
        }

        [Test]
        public void DifferentSource_IsNotMine()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, "OtherPiece", Code, MyAction, MySource, waitForPopup: false, now: 0f, fallbackSeconds: 0f);

            Assert.IsFalse(tracker.TryTakeRevert(0f));
        }

        [Test]
        public void EmptySource_IsAcceptedAsMine()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, sourceId: "", Code, MyAction, MySource, waitForPopup: false, now: 0f, fallbackSeconds: 0f);

            Assert.IsTrue(tracker.TryTakeRevert(0f));
        }

        [Test]
        public void ImmediateMode_RevertsWithoutWaitingForPopup()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: false, now: 10f, fallbackSeconds: 5f);

            Assert.IsTrue(tracker.TryTakeRevert(10f));
        }

        [Test]
        public void PopupGated_ReleasesOnMatchingReasonCode()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: true, now: 0f, fallbackSeconds: 10f);

            Assert.IsFalse(tracker.TryTakeRevert(0.1f), "must not revert before the popup closes");

            tracker.NotifyPopupClosed(Code);

            Assert.IsTrue(tracker.TryTakeRevert(0.1f));
        }

        [Test]
        public void PopupGated_DifferentReasonCodeDoesNotRelease()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: true, now: 0f, fallbackSeconds: 10f);

            tracker.NotifyPopupClosed("WRONG_ACTION");

            Assert.IsFalse(tracker.TryTakeRevert(0.1f));
        }

        [Test]
        public void PopupGated_FallbackExpiryReleasesEvenWithoutPopup()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: true, now: 0f, fallbackSeconds: 10f);

            Assert.IsFalse(tracker.TryTakeRevert(9.9f));
            Assert.IsTrue(tracker.TryTakeRevert(10f));
        }

        [Test]
        public void PopupGated_ZeroFallbackWaitsIndefinitely()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: true, now: 0f, fallbackSeconds: 0f);

            Assert.IsFalse(tracker.TryTakeRevert(1000f), "fallback of 0 means wait for the popup, not skip it");

            tracker.NotifyPopupClosed(Code);
            Assert.IsTrue(tracker.TryTakeRevert(1000f));
        }

        [Test]
        public void TryTakeRevert_YieldsTrueOnlyOnce()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: false, now: 0f, fallbackSeconds: 0f);

            Assert.IsTrue(tracker.TryTakeRevert(0f));
            Assert.IsFalse(tracker.TryTakeRevert(0f), "a revert already taken must not be handed out again");
        }

        [Test]
        public void Reset_ClearsAPendingRevert()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: true, now: 0f, fallbackSeconds: 10f);

            tracker.Reset();

            Assert.IsFalse(tracker.TryTakeRevert(1000f), "Reset must drop the pending refusal entirely");
        }

        [Test]
        public void SecondMatchingRefusalWhilePending_DoesNotRestartFallbackClock()
        {
            var tracker = new RefusedAttemptTracker();
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: true, now: 0f, fallbackSeconds: 10f);

            // A second refusal arrives at t=9 — must not push the deadline to t=19.
            tracker.Observe(MyAction, MySource, Code, MyAction, MySource, waitForPopup: true, now: 9f, fallbackSeconds: 10f);

            Assert.IsFalse(tracker.TryTakeRevert(9.9f));
            Assert.IsTrue(tracker.TryTakeRevert(10f), "the original deadline (t=10) must still govern");
        }
    }
}
