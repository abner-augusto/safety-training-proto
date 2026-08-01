#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>Bounded reliable FIFO plus one replaceable pose slot.</summary>
    public sealed class OutgoingMessageBuffer : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Queue<byte[]> _reliable = new Queue<byte[]>();
        private readonly int _capacity;
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private byte[]? _latestPose;
        private bool _completed;
        private bool _disposed;

        public OutgoingMessageBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int ReliableCount
        {
            get { lock (_gate) return _reliable.Count; }
        }

        public bool TryEnqueue(byte[] frame, bool droppable)
        {
            if (frame == null) return false;
            lock (_gate)
            {
                if (_completed) return false;
                if (droppable)
                {
                    _latestPose = frame;
                }
                else
                {
                    if (_reliable.Count >= _capacity) return false;
                    _reliable.Enqueue(frame);
                }
            }

            _signal.Release();
            return true;
        }

        public bool TryDequeue(out byte[]? frame)
        {
            lock (_gate)
            {
                if (_reliable.Count > 0)
                {
                    frame = _reliable.Dequeue();
                    return true;
                }

                if (_latestPose != null)
                {
                    frame = _latestPose;
                    _latestPose = null;
                    return true;
                }
            }

            frame = null;
            return false;
        }

        public void Wait(CancellationToken cancellationToken) => _signal.Wait(cancellationToken);

        public void Complete()
        {
            lock (_gate)
            {
                if (_completed) return;
                _completed = true;
            }
            _signal.Release();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _signal.Dispose();
        }
    }
}
