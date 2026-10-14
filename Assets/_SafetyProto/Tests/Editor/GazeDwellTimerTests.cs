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
            Tick(timer, gazed: false, seconds: 0.5f);

            Assert.AreEqual(0f, timer.Progress, 1e-4f);
            Assert.AreEqual(GazeDwellState.Idle, timer.State);
        }

        [Test]
        public void DrainRateIsIndependentOfFillLevel()
        {            var full = NewTimer();
            Tick(full, gazed: true, seconds: 1.9f);
            float fullBefore = full.Progress;

            var partial = NewTimer();
            Tick(partial, gazed: true, seconds: 0.6f);
            float partialBefore = partial.Progress;

            Assert.Greater(fullBefore, partialBefore + 0.5f,
                "Setup check: the two rings must start well apart in fill level.");
            const float drainWindow = Grace + 0.05f;
            const float drainStep = 0.02f;
            Tick(full, gazed: false, seconds: drainWindow, step: drainStep);
            Tick(partial, gazed: false, seconds: drainWindow, step: drainStep);

            float fullDelta = fullBefore - full.Progress;
            float partialDelta = partialBefore - partial.Progress;

            Assert.Greater(fullDelta, 0f, "Setup check: the drain window must actually remove progress.");
            Assert.AreEqual(fullDelta, partialDelta, 0.05f,
                "The same drain window must remove the same amount of progress regardless of " +
                "starting fill.");
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
