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

            detector.Prune(40f);

            var result = detector.RecordViolation(40f);

            Assert.IsFalse(result);
            Assert.AreEqual(1, detector.CurrentCount);
        }

        [Test]
        public void QueueRemainsBounded_WhenViolationsSpreadOut()
        {
            var detector = new SafetyPatternDetector(10f, 3);

            detector.RecordViolation(0f);
            detector.Prune(15f);
            detector.RecordViolation(15f);
            detector.Prune(30f);
            detector.RecordViolation(30f);

            Assert.AreEqual(1, detector.CurrentCount);
        }
    }
}
