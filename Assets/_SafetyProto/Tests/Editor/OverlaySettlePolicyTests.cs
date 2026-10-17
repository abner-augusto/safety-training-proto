using NUnit.Framework;
using SafetyProto.Runtime.Interaction;

namespace SafetyProto.Tests.Editor
{
    public class OverlaySettlePolicyTests
    {
        [Test]
        public void SettledOpaqueCanvasUsesTheLayer()
        {
            Assert.IsTrue(OverlaySettlePolicy.ShouldUseLayer(false, 1f, 0.3f, 0.3f));
        }

        [Test]
        public void FollowingCanvasStaysInTheEyeBuffer()
        {
            Assert.IsFalse(OverlaySettlePolicy.ShouldUseLayer(true, 1f, 5f, 0.3f));
        }

        [Test]
        public void FadingCanvasStaysInTheEyeBuffer()
        {
            Assert.IsFalse(OverlaySettlePolicy.ShouldUseLayer(false, 0.5f, 5f, 0.3f));
        }

        [Test]
        public void CanvasThatJustStoppedWaitsForTheSettleTime()
        {
            Assert.IsFalse(OverlaySettlePolicy.ShouldUseLayer(false, 1f, 0.1f, 0.3f));
        }
    }
}
