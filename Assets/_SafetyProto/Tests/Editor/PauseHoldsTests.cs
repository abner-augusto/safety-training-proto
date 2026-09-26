using NUnit.Framework;
using SafetyProto.Core.Events;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers the pause arbitration shared by every pause source: the session pauses on the
    /// first hold and resumes only when the last one is released, so one source resuming can
    /// never cancel a pause another source still needs.
    /// </summary>
    public class PauseHoldsTests
    {
        [Test]
        public void FirstHoldPauses()
        {
            var holds = new PauseHolds();
            Assert.IsTrue(holds.Acquire(PauseSource.ApplicationFocus));
            Assert.IsTrue(holds.IsPaused);
        }

        [Test]
        public void SecondSourceDoesNotPauseAgain()
        {
            var holds = new PauseHolds();
            holds.Acquire(PauseSource.Popup);
            Assert.IsFalse(holds.Acquire(PauseSource.ApplicationFocus));
        }

        [Test]
        public void ReleasingOneSourceKeepsPauseWhileAnotherHolds()
        {
            var holds = new PauseHolds();
            holds.Acquire(PauseSource.Popup);
            holds.Acquire(PauseSource.ApplicationFocus);

            Assert.IsFalse(holds.Release(PauseSource.ApplicationFocus));
            Assert.IsTrue(holds.IsPaused);
        }

        [Test]
        public void ReleasingLastSourceResumes()
        {
            var holds = new PauseHolds();
            holds.Acquire(PauseSource.Popup);
            holds.Acquire(PauseSource.PauseMenu);
            holds.Release(PauseSource.Popup);

            Assert.IsTrue(holds.Release(PauseSource.PauseMenu));
            Assert.IsFalse(holds.IsPaused);
        }

        [Test]
        public void RepeatedHoldFromSameSourceNeedsOneRelease()
        {
            var holds = new PauseHolds();
            holds.Acquire(PauseSource.PauseMenu);
            Assert.IsFalse(holds.Acquire(PauseSource.PauseMenu));

            Assert.IsTrue(holds.Release(PauseSource.PauseMenu));
            Assert.IsFalse(holds.IsPaused);
        }

        [Test]
        public void ReleasingUnheldSourceChangesNothing()
        {
            var holds = new PauseHolds();
            Assert.IsFalse(holds.Release(PauseSource.Popup));

            holds.Acquire(PauseSource.ApplicationFocus);
            Assert.IsFalse(holds.Release(PauseSource.Popup));
            Assert.IsTrue(holds.IsPaused);
        }

        [Test]
        public void ClearDropsEveryHold()
        {
            var holds = new PauseHolds();
            holds.Acquire(PauseSource.Popup);
            holds.Acquire(PauseSource.ApplicationFocus);
            holds.Clear();

            Assert.IsFalse(holds.IsPaused);
            Assert.IsTrue(holds.Acquire(PauseSource.PauseMenu));
        }
    }
}
