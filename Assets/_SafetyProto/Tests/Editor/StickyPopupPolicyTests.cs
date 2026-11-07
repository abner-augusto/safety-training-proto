using NUnit.Framework;
using SafetyProto.Domain.Feedback;

namespace SafetyProto.Tests.Editor
{
    /// <summary>A feedback popup never disappears before the participant had the chance to see it.</summary>
    public class StickyPopupPolicyTests
    {
        private StickyPopupPolicy _popup = null!;

        [SetUp]
        public void Setup() => _popup = new StickyPopupPolicy(shownAt: 10f, readSeconds: 6f, maxSeconds: 30f);

        [Test]
        public void WithoutAction_StaysUntilTheCeiling()
        {
            Assert.IsFalse(_popup.ShouldClose(16f), "the old popup closed here");
            Assert.IsFalse(_popup.ShouldClose(39.9f));
            Assert.IsTrue(_popup.ShouldClose(40f));
        }

        [Test]
        public void HoldsTheClockOnlyForTheReadingTime()
        {
            Assert.IsTrue(_popup.HoldsClock(15.9f));
            Assert.IsFalse(_popup.HoldsClock(16f));
        }

        [Test]
        public void ActionAfterReading_ClosesIt()
        {
            _popup.NotifyAction(20f);

            Assert.IsTrue(_popup.ShouldClose(20f));
        }

        [Test]
        public void ActionWhileReading_ClosesItWhenTheReadingTimeEnds()
        {
            _popup.NotifyAction(12f);

            Assert.IsFalse(_popup.ShouldClose(15f));
            Assert.IsTrue(_popup.ShouldClose(16f));
        }

        [Test]
        public void TheActionThatOpenedThePopup_DoesNotCount()
        {
            _popup.NotifyAction(10.2f);

            Assert.IsFalse(_popup.ShouldClose(20f));
        }

        [Test]
        public void CeilingShorterThanReading_IsRaisedToIt()
        {
            var popup = new StickyPopupPolicy(0f, readSeconds: 8f, maxSeconds: 5f);

            Assert.IsFalse(popup.ShouldClose(7f));
            Assert.IsTrue(popup.ShouldClose(8f));
        }
    }
}
