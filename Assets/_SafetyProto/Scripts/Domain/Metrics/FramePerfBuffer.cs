#nullable enable
using System.Threading;

namespace SafetyProto.Domain.Metrics
{
    /// <summary>One rendered frame: the EventBus flush it ran, if any, and the CPU/GPU timings
    /// reported for the latest frame the engine had finished measuring.</summary>
    public struct FramePerfRow
    {
        /// <summary>Same clock as the event timestamps in the session log (Unix ms, UTC).</summary>
        public long UtcMs;
        public int Frame;
        /// <summary>Flush duration in Stopwatch ticks; 0 on a frame without a flush.</summary>
        public int FlushTicks;
        /// <summary>Events dispatched by the flush; 0 means no flush ran this frame.</summary>
        public int Dispatched;
        public int QueueBefore;
        public float MainThreadMs;
        public float PresentWaitMs;
        public float RenderThreadMs;
        public float GpuMs;
        public bool CutByBudget;

        /// <summary>Timing fields hold this when the engine reported no timing.</summary>
        public const float Unavailable = -1f;
    }

    /// <summary>
    /// Fixed-capacity, append-only row store, allocated once so recording a frame never
    /// allocates. A single writer appends on the main thread; readers on other threads see only
    /// rows below the <see cref="Count"/> they read, which are never modified until
    /// <see cref="Clear"/>.
    /// </summary>
    public sealed class FramePerfBuffer
    {
        private readonly FramePerfRow[] _rows;
        private int _count;

        public FramePerfBuffer(int capacity)
        {
            _rows = new FramePerfRow[capacity < 1 ? 1 : capacity];
        }

        public int Capacity => _rows.Length;

        public int Count => Volatile.Read(ref _count);

        public bool IsFull => Count >= _rows.Length;

        public ref readonly FramePerfRow this[int index] => ref _rows[index];

        /// <summary>Returns false, dropping the row, once the buffer is full.</summary>
        public bool TryAppend(in FramePerfRow row)
        {
            int count = _count;
            if (count >= _rows.Length) return false;
            _rows[count] = row;
            Volatile.Write(ref _count, count + 1);
            return true;
        }

        public void Clear() => Volatile.Write(ref _count, 0);
    }
}
