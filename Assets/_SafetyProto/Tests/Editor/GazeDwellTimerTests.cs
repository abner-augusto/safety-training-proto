using NUnit.Framework;
using SafetyProto.Runtime.Interaction;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers the dwell accumulator: how it fills, how much gaze loss it forgives before
    /// draining, how fast it drains, and that completion latches exactly once.
    /// </summary>
    public class GazeDwellTimerTests
    {
        private const float Dwell = 2f;
        private const float Grace = 0.2f;
        private const float Decay = 0.3f;

        private static GazeDwellTimer NewTimer() => new GazeDwellTimer(Dwell, Grace, Decay);

        private static void Tick(GazeDwellTimer timer, bool gazed, float seconds, float step = 0.05f)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += step)
                timer.Tick(gazed, step);
        }

        [Test]
        public void StartsIdleAtZeroProgress()
        {
            var timer = NewTimer();
            Assert.AreEqual(GazeDwellState.Idle, timer.State);
            Assert.AreEqual(0f, timer.Progress, 1e-4f);
        }

        [Test]
        public void FillsProportionallyWhileGazed()
        {
            var timer = NewTimer();
            Tick(timer, gazed: true, seconds: 1f);
            Assert.AreEqual(GazeDwellState.Dwelling, timer.State);
            Assert.AreEqual(0.5f, timer.Progress, 0.05f);
        }

        [Test]
        public void DoesNotCompleteBeforeDwellDuration()
        {
            var timer = NewTimer();
            Tick(timer, gazed: true, seconds: 1.9f);
            Assert.AreNotEqual(GazeDwellState.Completed, timer.State);
        }

        [Test]
        public void CompletesAfterDwellDuration()
        {
            var timer = NewTimer();
            Tick(timer, gazed: true, seconds: 2.1f);
            Assert.AreEqual(GazeDwellState.Completed, timer.State);
            Assert.AreEqual(1f, timer.Progress, 1e-4f);
        }

        [Test]
        public void BriefGazeLossWithinGraceDoesNotDrain()
        {
            var timer = NewTimer();
            Tick(timer, gazed: true, seconds: 1f);
            float before = timer.Progress;

            Tick(timer, gazed: false, seconds: 0.15f);

            Assert.AreEqual(before, timer.Progress, 1e-4f,
                "Head jitter shorter than the grace window must not move the ring at all.");
        }

        [Test]
        public void GazeLossBeyondGraceDrainsToIdle()
        {
            var timer = NewTimer();
            Tick(timer, gazed: true, seconds: 1f);

            // 0.2s grace, then a full ring drains in 0.3s — half a ring needs 0.15s.
            Tick(timer, gazed: false, seconds: 0.5f);

            Assert.AreEqual(0f, timer.Progress, 1e-4f);
            Assert.AreEqual(GazeDwellState.Idle, timer.State);
        }

        [Test]
        public void DrainRateIsIndependentOfFillLevel()
        {
            // 2.1s, not 2.0s: forty float additions of 0.05 land just under 2.0, so an exact-length
            // gaze would leave the dwell one epsilon short of completing.
            var timer = NewTimer();
            Tick(timer, gazed: true, seconds: 2.1f);        // full
            Tick(timer, gazed: false, seconds: Grace + Decay + 0.05f);
            Assert.AreEqual(GazeDwellState.Completed, timer.State,
                "A completed dwell latches and never drains.");

            var partial = NewTimer();
            Tick(partial, gazed: true, seconds: 0.6f);
            Tick(partial, gazed: false, seconds: Grace + Decay + 0.05f);
            Assert.AreEqual(0f, partial.Progress, 1e-4f);
        }

        [Test]
        public void CompletionIsReportedExactlyOnce()
        {
            var timer = NewTimer();
            int completions = 0;
            for (float t = 0f; t < 4f; t += 0.05f)
                if (timer.Tick(true, 0.05f)) completions++;

            Assert.AreEqual(1, completions);
        }

        [Test]
        public void ResetReturnsToIdle()
        {
            var timer = NewTimer();
            Tick(timer, gazed: true, seconds: 2.1f);
            timer.Reset();
            Assert.AreEqual(GazeDwellState.Idle, timer.State);
            Assert.AreEqual(0f, timer.Progress, 1e-4f);
        }
    }
}
