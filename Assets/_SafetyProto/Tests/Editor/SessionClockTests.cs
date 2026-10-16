using NUnit.Framework;
using SafetyProto.Domain.Metrics;

namespace SafetyProto.Tests.Editor
{
    public class SessionClockTests
    {
        [Test]
        public void TicksBeforeStartDoNotAdvance()
        {
            var clock = new SessionClock();
            clock.Tick(1f, paused: false);
            Assert.AreEqual(0f, clock.ElapsedSeconds);
        }

        [Test]
        public void PausedTicksDoNotAdvance()
        {
            var clock = new SessionClock();
            clock.Start();
            clock.Tick(2f, paused: true);
            Assert.AreEqual(0f, clock.ElapsedSeconds);
        }

        [Test]
        public void RunningPausedRunningCountsOnlyTheRunningTime()
        {
            var clock = new SessionClock();
            clock.Start();
            clock.Tick(10f, paused: false);
            clock.Tick(5f, paused: true);
            clock.Tick(10f, paused: false);
            Assert.AreEqual(20f, clock.ElapsedSeconds, 1e-4f);
        }

        [Test]
        public void ResetZeroesAndStops()
        {
            var clock = new SessionClock();
            clock.Start();
            clock.Tick(3f, paused: false);
            clock.Reset();
            clock.Tick(3f, paused: false);
            Assert.AreEqual(0f, clock.ElapsedSeconds);
            Assert.IsFalse(clock.IsRunning);
        }

        [Test]
        public void StopFreezesWithoutLosingTheValue()
        {
            var clock = new SessionClock();
            clock.Start();
            clock.Tick(4f, paused: false);
            clock.Stop();
            clock.Tick(4f, paused: false);
            Assert.AreEqual(4f, clock.ElapsedSeconds);
        }
    }
}
