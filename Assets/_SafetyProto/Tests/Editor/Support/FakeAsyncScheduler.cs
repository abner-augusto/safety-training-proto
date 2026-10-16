#nullable enable
using System.Threading;
using System.Threading.Tasks;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Tests.Editor.Support
{
    /// <summary>
    /// Hand-driven <see cref="IAsyncScheduler"/>. The pending delay completes only when the test
    /// says so, and continuations run inline on the calling thread, which makes the
    /// orchestrator's continuation deterministic. Both legal cancellation behaviours are
    /// available: with <see cref="ThrowOnCancel"/> the delay faults with
    /// <see cref="System.OperationCanceledException"/> the moment the token is cancelled;
    /// without it, cancellation is ignored and the delay completes normally on
    /// <see cref="Complete"/>, which is what an adapter that swallows the cancellation does.
    /// </summary>
    public sealed class FakeAsyncScheduler : IAsyncScheduler
    {
        private TaskCompletionSource<bool>? _pending;

        public bool ThrowOnCancel { get; set; } = true;
        public int DelayCalls { get; private set; }
        public bool HasPending => _pending != null && !_pending.Task.IsCompleted;

        public Task DelayAsync(float seconds, CancellationToken cancellationToken)
        {
            DelayCalls++;
            var tcs = new TaskCompletionSource<bool>();
            _pending = tcs;
            if (ThrowOnCancel)
                cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return tcs.Task;
        }

        /// <summary>Lets the pending delay elapse normally.</summary>
        public void Complete() => _pending?.TrySetResult(true);
    }
}
