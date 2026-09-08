using NUnit.Framework;
using SafetyProto.Runtime.Analytics;

namespace SafetyProto.Tests.Editor
{
    public class SafetyPatternTests
    {
        [Test]
        public void DetectsPattern_WhenViolationsWithinWindow_ReturnsTrue()
        {
            var detector = new SafetyPatternDetector(30f, 3);

            Assert.IsFalse(detector.RecordViolation(0f));
            Assert.IsFalse(detector.RecordViolation(5f));
            var result = detector.RecordViolation(10f);

            Assert.IsTrue(result);
            Assert.AreEqual(3, detector.CurrentCount);
        }

        [Test]
        public void RespectsWindow_WhenOldViolationsExpire_PatternNotDetected()
        {
            var detector = new SafetyPatternDetector(30f, 3);

            detector.RecordViolation(0f);
            detector.RecordViolation(5f);

            // Deliberately NO manual Prune() call here — RecordViolation must prune internally
            // on its own (it does, via its own Prune(time) call). Calling Prune explicitly right
            // before this would mask a regression where RecordViolation's internal pruning was
            // deleted: the manual call would still clean the queue and these assertions would
            // pass regardless of whether the production code prunes at all.
            var result = detector.RecordViolation(40f);

            Assert.IsFalse(result);
            Assert.AreEqual(1, detector.CurrentCount);
        }

        [Test]
        public void QueueRemainsBounded_WhenViolationsSpreadOut()
        {
            var detector = new SafetyPatternDetector(10f, 3);

            // No manual Prune() calls — three RecordViolation calls spread past the window, each
            // relying on RecordViolation's own internal pruning to keep the queue bounded.
            detector.RecordViolation(0f);
            detector.RecordViolation(15f);
            detector.RecordViolation(30f);

            Assert.AreEqual(1, detector.CurrentCount);
        }
    }
}
