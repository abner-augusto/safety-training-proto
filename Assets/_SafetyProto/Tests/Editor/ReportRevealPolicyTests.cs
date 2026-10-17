using NUnit.Framework;
using SafetyProto.Domain.Sessions;

namespace SafetyProto.Tests.Editor
{
    public class ReportRevealPolicyTests
    {
        [Test]
        public void WaitsWhileThePopupIsDisplayed()
        {
            Assert.IsFalse(ReportRevealPolicy.ShouldReveal(popupDisplayed: true, waitedSeconds: 0.1f));
        }

        [Test]
        public void RevealsAsSoonAsThePopupIsGone()
        {
            Assert.IsTrue(ReportRevealPolicy.ShouldReveal(popupDisplayed: false, waitedSeconds: 0f));
        }

        [Test]
        public void RevealsAnywayAtTheCapSoAStuckPopupCannotHideTheReport()
        {
            Assert.IsTrue(ReportRevealPolicy.ShouldReveal(popupDisplayed: true, waitedSeconds: ReportRevealPolicy.MaxWaitSeconds));
        }
    }
}
