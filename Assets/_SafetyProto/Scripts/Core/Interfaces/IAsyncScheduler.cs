using System.Threading;
using System.Threading.Tasks;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Async delay abstraction for Core logic that needs to wait without depending on
    /// <c>UnityEngine.Awaitable</c>. When the token is cancelled the implementation may either
    /// throw <see cref="System.OperationCanceledException"/> or complete early; callers must
    /// check the token after awaiting and treat both as "do not continue". The Unity adapter
    /// wraps <c>Awaitable.WaitForSecondsAsync</c>; the CLI harness passes no scheduler and
    /// advances immediately.
    /// </summary>
    public interface IAsyncScheduler
    {
        Task DelayAsync(float seconds, CancellationToken cancellationToken);
    }
}
