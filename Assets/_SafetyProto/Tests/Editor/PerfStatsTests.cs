using System.IO;
using NUnit.Framework;
using SafetyProto.Domain.Metrics;
using SafetyProto.Domain.Sessions;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Locks the perf aggregation the dissertation reads: percentile definition, which frames
    /// count as flushes, and the CSV/file-name contract shared with the session log.
    /// </summary>
    public class PerfStatsTests
    {
        private const long MicrosecondTicks = 1_000_000;

        private static FramePerfRow Flush(int us, int dispatched, bool cut = false, float mainMs = 10f) =>
            new FramePerfRow
            {
                FlushTicks = us,
                Dispatched = dispatched,
                QueueBefore = dispatched,
                CutByBudget = cut,
                MainThreadMs = mainMs,
                PresentWaitMs = FramePerfRow.Unavailable,
                RenderThreadMs = FramePerfRow.Unavailable,
                GpuMs = FramePerfRow.Unavailable
            };

        private static FramePerfRow Idle(float mainMs = 10f) => Flush(0, 0, mainMs: mainMs);

        [Test]
        public void NearestRank_PicksTheValueAtCeilingRank()
        {
            var sorted = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

            Assert.AreEqual(5d, PerfStats.NearestRank(sorted, 10, 0.50));
            Assert.AreEqual(10d, PerfStats.NearestRank(sorted, 10, 0.95));
            Assert.AreEqual(10d, PerfStats.NearestRank(sorted, 10, 0.99));
            Assert.AreEqual(1d, PerfStats.NearestRank(sorted, 10, 0.01));
        }

        [Test]
        public void NearestRank_OnlyReadsTheFirstCountValues()
        {
            var sorted = new double[] { 1, 2, 3, 999 };

            Assert.AreEqual(3d, PerfStats.NearestRank(sorted, 3, 0.99));
        }

        [Test]
        public void NearestRank_EmptyIsZero()
        {
            Assert.AreEqual(0d, PerfStats.NearestRank(new double[0], 0, 0.5));
        }

        [Test]
        public void TicksToMicroseconds_UsesTheGivenFrequency()
        {
            Assert.AreEqual(1.5d, PerfStats.TicksToMicroseconds(1500, 1_000_000_000), 1e-9);
            Assert.AreEqual(150d, PerfStats.TicksToMicroseconds(1500, 10_000_000), 1e-9);
        }

        [Test]
        public void Summarize_CountsOnlyFramesThatDispatched()
        {
            var buffer = new FramePerfBuffer(16);
            buffer.TryAppend(Idle());
            buffer.TryAppend(Flush(40, 2));
            buffer.TryAppend(Idle());
            buffer.TryAppend(Flush(10, 1));
            buffer.TryAppend(Flush(30, 5, cut: true));

            var summary = PerfStats.Summarize(buffer, MicrosecondTicks);

            Assert.AreEqual(5, summary.framesRecorded);
            Assert.AreEqual(3, summary.framesWithFlush);
            Assert.AreEqual(8, summary.totalEvents);
            Assert.AreEqual(1, summary.budgetCutFrames);
            Assert.AreEqual(30d, summary.flushMedianUs);
            Assert.AreEqual(40d, summary.flushP95Us);
            Assert.AreEqual(40d, summary.flushP99Us);
            Assert.AreEqual(40d, summary.flushMaxUs);
            Assert.IsFalse(summary.truncated);
        }

        [Test]
        public void Summarize_MainThreadUsesEveryTimedFrame()
        {
            var buffer = new FramePerfBuffer(32);
            for (int i = 1; i <= 20; i++) buffer.TryAppend(Idle(mainMs: i));
            buffer.TryAppend(Idle(mainMs: FramePerfRow.Unavailable));

            var summary = PerfStats.Summarize(buffer, MicrosecondTicks);

            Assert.IsTrue(summary.frameTimingAvailable);
            Assert.AreEqual(10d, summary.mainThreadMedianMs);
            Assert.AreEqual(19d, summary.mainThreadP95Ms);
            Assert.AreEqual(0, summary.framesWithFlush);
        }

        [Test]
        public void Summarize_WithoutFrameTimingLeavesMainThreadUnset()
        {
            var buffer = new FramePerfBuffer(4);
            buffer.TryAppend(Flush(5, 1, mainMs: FramePerfRow.Unavailable));

            var summary = PerfStats.Summarize(buffer, MicrosecondTicks);

            Assert.IsFalse(summary.frameTimingAvailable);
            Assert.IsNull(summary.mainThreadMedianMs);
            Assert.IsNull(summary.mainThreadP95Ms);
        }

        [Test]
        public void Buffer_DropsRowsOnceFullAndReportsTruncation()
        {
            var buffer = new FramePerfBuffer(2);

            Assert.IsTrue(buffer.TryAppend(Idle()));
            Assert.IsTrue(buffer.TryAppend(Idle()));
            Assert.IsFalse(buffer.TryAppend(Idle()));
            Assert.AreEqual(2, buffer.Count);
            Assert.IsTrue(PerfStats.Summarize(buffer, MicrosecondTicks).truncated);

            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void Csv_WritesHeaderColumnsAndBlankUnavailableTimings()
        {
            var buffer = new FramePerfBuffer(4);
            var row = Flush(1500, 3, cut: true, mainMs: 7.25f);
            row.UtcMs = 1760000000123;
            row.Frame = 42;
            buffer.TryAppend(row);

            var writer = new StringWriter { NewLine = "\n" };
            FramePerfCsv.WriteHeader(writer, new FramePerfCsvHeader
            {
                SessionId = "abc", ParticipantId = "P-07", AppVersion = "1.2.4", Commit = "2e11262",
                StopwatchFrequency = 1_000_000_000
            });
            FramePerfCsv.WriteRows(writer, buffer, 0, 1, 1_000_000_000);

            var lines = writer.ToString().TrimEnd('\n').Split('\n');
            StringAssert.StartsWith("# format=", lines[0]);
            CollectionAssert.Contains(lines, "# participantId=P-07");
            CollectionAssert.Contains(lines, "# commit=2e11262");
            CollectionAssert.Contains(lines, "# stopwatchHz=1000000000");
            Assert.AreEqual(FramePerfCsv.Columns, lines[lines.Length - 2]);
            Assert.AreEqual("1760000000123,42,1.5,3,3,1,7.25,,,", lines[lines.Length - 1]);
        }

        [Test]
        public void FileStem_MatchesTheSessionLogNaming()
        {            Assert.AreEqual("20251009_085320_d9a2af99", SessionFileStem.Of(1759999999999 + 1, "d9a2af99-1234-5678"));
            Assert.AreEqual("20251009_085320", SessionFileStem.Of(1760000000000, ""));
        }
    }
}
