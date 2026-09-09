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
            // Progress drains at a constant per-second rate (1 / decaySeconds), not a rate
            // proportional to the current fill — the class doc calls this out explicitly.
            // Comparing the delta lost by two DIFFERENT starting fill levels over the SAME
            // short drain window is what actually exercises that claim: letting either ring
            // reach an extreme (Completed, which never drains at all; or fully back to Idle,
            // i.e. clamped at 0) erases the very difference a rate comparison needs.
            var full = NewTimer();
            Tick(full, gazed: true, seconds: 1.9f);          // ~0.95 progress, still Dwelling
            float fullBefore = full.Progress;

            var partial = NewTimer();
            Tick(partial, gazed: true, seconds: 0.6f);       // ~0.30 progress
            float partialBefore = partial.Progress;

            Assert.Greater(fullBefore, partialBefore + 0.5f,
                "Setup check: the two rings must start well apart in fill level.");

            // Grace (0.2s), then a short 0.05s of actual drain — well short of fully draining
            // either ring, so both remain comparable afterward. The step must be finer than the
            // 0.05s default: grace is consumed a whole step at a time and its float residue
            // survives four 0.05s subtractions, which would eat the entire drain window and
            // leave both deltas at zero.
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
