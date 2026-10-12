#nullable enable
using System.Globalization;
using System.IO;

namespace SafetyProto.Domain.Metrics
{
    /// <summary>Identifies the session and build a perf CSV belongs to.</summary>
    public sealed class FramePerfCsvHeader
    {
        public string SessionId = string.Empty;
        public string ParticipantId = string.Empty;
        public string AppVersion = string.Empty;
        public string Commit = string.Empty;
        public long StopwatchFrequency;
    }

    /// <summary>
    /// Perf CSV layout: "# key=value" metadata lines, then a column header, then one row per
    /// frame. Readers skip the metadata with a comment marker (pandas: <c>comment="#"</c>).
    /// </summary>
    public static class FramePerfCsv
    {
        public const string Format = "safetyproto-perf/1";

        public const string Columns =
            "utc_ms,frame,flush_us,dispatched,queue_before,budget_cut,main_ms,present_wait_ms,render_ms,gpu_ms";

        public static void WriteHeader(TextWriter writer, FramePerfCsvHeader header)
        {
            writer.WriteLine("# format=" + Format);
            writer.WriteLine("# sessionId=" + header.SessionId);
            writer.WriteLine("# participantId=" + header.ParticipantId);
            writer.WriteLine("# appVersion=" + header.AppVersion);
            writer.WriteLine("# commit=" + header.Commit);
            writer.WriteLine("# stopwatchHz=" + header.StopwatchFrequency.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("# frameTiming=FrameTimingManager latest available frame; lags the row's frame by a few frames; empty when not reported");
            writer.WriteLine(Columns);
        }

        /// <summary>Writes rows [<paramref name="from"/>, <paramref name="to"/>).</summary>
        public static void WriteRows(TextWriter writer, FramePerfBuffer buffer, int from, int to, long stopwatchFrequency)
        {
            var inv = CultureInfo.InvariantCulture;
            for (int i = from; i < to; i++)
            {
                ref readonly var row = ref buffer[i];
                writer.Write(row.UtcMs.ToString(inv));
                writer.Write(',');
                writer.Write(row.Frame.ToString(inv));
                writer.Write(',');
                writer.Write(PerfStats.TicksToMicroseconds(row.FlushTicks, stopwatchFrequency).ToString("0.##", inv));
                writer.Write(',');
                writer.Write(row.Dispatched.ToString(inv));
                writer.Write(',');
                writer.Write(row.QueueBefore.ToString(inv));
                writer.Write(',');
                writer.Write(row.CutByBudget ? '1' : '0');
                WriteTiming(writer, row.MainThreadMs, inv);
                WriteTiming(writer, row.PresentWaitMs, inv);
                WriteTiming(writer, row.RenderThreadMs, inv);
                WriteTiming(writer, row.GpuMs, inv);
                writer.WriteLine();
            }
        }

        private static void WriteTiming(TextWriter writer, float ms, CultureInfo inv)
        {
            writer.Write(',');
            if (ms >= 0f) writer.Write(ms.ToString("0.###", inv));
        }
    }
}
