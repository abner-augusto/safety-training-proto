using NUnit.Framework;
using SafetyProto.Domain.Tasks;

namespace SafetyProto.Tests.Editor
{
    public class PhaseAdvanceGateTests
    {
        [Test]
        public void TargetGroupStillRunning_ClosesItThenAdvances()
        {
            Assert.AreEqual(PhaseAdvanceAction.CloseThenAdvance,
                PhaseAdvanceGate.Decide("ppe_selection", "ppe_selection", targetGroupCompleted: false));
        }

        [Test]
        public void TargetGroupAlreadyCompleted_AdvancesWithoutClosingAnything()
        {            Assert.AreEqual(PhaseAdvanceAction.AdvanceOnly,
                PhaseAdvanceGate.Decide("scaffold_inspection", "ppe_selection", targetGroupCompleted: true));
        }

        [Test]
        public void TargetGroupNeitherCurrentNorCompleted_IsIgnored()
        {
            Assert.AreEqual(PhaseAdvanceAction.Ignore,
                PhaseAdvanceGate.Decide("scaffold_inspection", "ppe_selection", targetGroupCompleted: false));
        }

        [Test]
        public void NoCurrentGroup_FallsBackToCompletionState()
        {
            Assert.AreEqual(PhaseAdvanceAction.Ignore,
                PhaseAdvanceGate.Decide(null, "ppe_selection", targetGroupCompleted: false));
            Assert.AreEqual(PhaseAdvanceAction.AdvanceOnly,
                PhaseAdvanceGate.Decide(null, "ppe_selection", targetGroupCompleted: true));
        }

        [Test]
        public void UnconfiguredTargetGroup_IsIgnored()
        {
            Assert.AreEqual(PhaseAdvanceAction.Ignore,
                PhaseAdvanceGate.Decide("ppe_selection", "", targetGroupCompleted: true));
            Assert.AreEqual(PhaseAdvanceAction.Ignore,
                PhaseAdvanceGate.Decide(null, null, targetGroupCompleted: true));
        }

        [Test]
        public void GroupIdComparisonIsCaseSensitive()
        {            Assert.AreEqual(PhaseAdvanceAction.Ignore,
                PhaseAdvanceGate.Decide("PPE_Selection", "ppe_selection", targetGroupCompleted: false));
        }
    }
}
