#nullable enable
using System;
using Newtonsoft.Json;

namespace SafetyProto.Domain.Metrics
{
    /// <summary>
    /// The perf block of the session log summary: EventBus flush cost and main-thread frame
    /// time over one session. Zeros are real measurements here, so every field is written even
    /// when it holds its default.
    /// </summary>
    [Serializable]
    public sealed class PerfSummary
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public int framesRecorded;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public int framesWithFlush;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public long totalEvents;
        /// <summary>Frames whose flush the per-frame budget cut short, leaving events queued.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public int budgetCutFrames;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public double flushMedianUs;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public double flushP95Us;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public double flushP99Us;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public double flushMaxUs;
        /// <summary>False when the engine reported no frame timing; the main-thread fields are
        /// then omitted.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public bool frameTimingAvailable;
        // Newtonsoft writes these; Unity never serializes this type. [NonSerialized] would make Newtonsoft drop them.
#pragma warning disable UAC1001
        public double? mainThreadMedianMs;
        public double? mainThreadP95Ms;
#pragma warning restore UAC1001
        /// <summary>True when the buffer filled before the session ended; later frames are missing.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)] public bool truncated;
        public string percentileMethod = PerfStats.PercentileMethod;
    }

    public static class PerfStats
    {
        public const string PercentileMethod = "nearest-rank";

        /// <summary>Nearest-rank percentile of the first <paramref name="count"/> values of an
        /// ascending array: the value at rank ceil(p·n). <paramref name="p"/> is in (0, 1].</summary>
        public static double NearestRank(double[] sortedAscending, int count, double p)
        {
            if (count <= 0) return 0d;
            int rank = (int)Math.Ceiling(p * count);
            if (rank < 1) rank = 1;
            if (rank > count) rank = count;
            return sortedAscending[rank - 1];
        }

        public static double TicksToMicroseconds(long ticks, long stopwatchFrequency) =>
            stopwatchFrequency > 0 ? ticks * 1_000_000d / stopwatchFrequency : 0d;

        /// <summary>Aggregates every row in <paramref name="buffer"/>. Allocates and sorts two
        /// arrays, so call it once per session, not per frame.</summary>
        public static PerfSummary Summarize(FramePerfBuffer buffer, long stopwatchFrequency)
        {
            int count = buffer.Count;
            var flushUs = new double[count];
            var mainMs = new double[count];
            int flushes = 0;
            int timed = 0;
            var summary = new PerfSummary { framesRecorded = count, truncated = buffer.IsFull };

            for (int i = 0; i < count; i++)
            {
                ref readonly var row = ref buffer[i];
                if (row.Dispatched > 0)
                {
                    flushUs[flushes++] = TicksToMicroseconds(row.FlushTicks, stopwatchFrequency);
                    summary.totalEvents += row.Dispatched;
                    if (row.CutByBudget) summary.budgetCutFrames++;
                }
                if (row.MainThreadMs >= 0f) mainMs[timed++] = row.MainThreadMs;
            }

            summary.framesWithFlush = flushes;
            if (flushes > 0)
            {
                Array.Sort(flushUs, 0, flushes);
                summary.flushMedianUs = Round(NearestRank(flushUs, flushes, 0.50), 2);
                summary.flushP95Us = Round(NearestRank(flushUs, flushes, 0.95), 2);
                summary.flushP99Us = Round(NearestRank(flushUs, flushes, 0.99), 2);
                summary.flushMaxUs = Round(flushUs[flushes - 1], 2);
            }

            summary.frameTimingAvailable = timed > 0;
            if (timed > 0)
            {
                Array.Sort(mainMs, 0, timed);
                summary.mainThreadMedianMs = Round(NearestRank(mainMs, timed, 0.50), 3);
                summary.mainThreadP95Ms = Round(NearestRank(mainMs, timed, 0.95), 3);
            }

            return summary;
        }

        private static double Round(double value, int digits) => Math.Round(value, digits);
    }
}
