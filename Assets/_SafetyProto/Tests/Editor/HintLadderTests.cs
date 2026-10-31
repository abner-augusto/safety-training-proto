using NUnit.Framework;
using SafetyProto.Domain.Feedback;

namespace SafetyProto.Tests.Editor
{
    /// <summary>Help by time without progress: 15 s pulse, 30 s narration, 60 s evaluator alert.</summary>
    public class HintLadderTests
    {
        private HintLadder _ladder = null!;

        [SetUp]
        public void Setup() => _ladder = new HintLadder(15f, 30f, 60f);

        /// <summary>Feeds <paramref name="seconds"/> in quarter-second frames; returns the levels reached, in order.</summary>
        private string Run(float seconds)
        {
            var reached = string.Empty;
            for (float t = 0f; t < seconds - 1e-4f; t += 0.25f)
            {
                int level = _ladder.Advance(0.25f);
                if (level > 0) reached += level;
            }
            return reached;
        }

        [Test]
        public void NoProgress_ReachesTheThreeLevelsInOrder()
        {
            Assert.AreEqual(string.Empty, Run(14.75f));
            Assert.AreEqual("1", Run(0.25f));
            Assert.AreEqual("2", Run(15f));
            Assert.AreEqual("3", Run(30f));
            Assert.AreEqual(string.Empty, Run(120f), "no level beyond the last");
        }

        [Test]
        public void Progress_CancelsTheHelpAndRestartsTheCount()
        {
            Run(31f);
            Assert.AreEqual(2, _ladder.Level);

            _ladder.Progress();

            Assert.AreEqual(0, _ladder.Level);
            Assert.AreEqual(0f, _ladder.SecondsWithoutProgress);
            Assert.AreEqual(string.Empty, Run(14f));
            Assert.AreEqual("1", Run(1f));
        }

        [Test]
        public void ALongFrame_ReachesOneLevelPerCall()
        {
            Assert.AreEqual(1, _ladder.Advance(40f));
            Assert.AreEqual(2, _ladder.Advance(0f));
            Assert.AreEqual(0, _ladder.Advance(0f));
        }

        [Test]
        public void SecondsWithoutProgress_IsReportedForTheLog()
        {
            Run(30f);

            Assert.AreEqual(30f, _ladder.SecondsWithoutProgress, 0.01f);
        }
    }
}
