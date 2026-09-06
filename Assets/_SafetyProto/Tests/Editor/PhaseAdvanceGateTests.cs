// Assets/_SafetyProto/Tests/Editor/PhaseAdvanceGateTests.cs
//
// Decision rule behind the Phase 1 advance button (modo Avaliação). The regression it guards:
// a participant who performed every PPE task found the button dead, because the group had
// already completed and the core had moved on to the next one, while a participant who skipped
// a task found it working.
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
        {
            // The participant did every task: the core is on the next group now, and closing
            // "the current group" would mark the NEXT group's tasks as not performed.
            Assert.AreEqual(PhaseAdvanceAction.AdvanceOnly,
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
        {
            // Ids are data keys, matched the same way the rest of the pipeline matches them.
            Assert.AreEqual(PhaseAdvanceAction.Ignore,
                PhaseAdvanceGate.Decide("PPE_Selection", "ppe_selection", targetGroupCompleted: false));
        }
    }
}
