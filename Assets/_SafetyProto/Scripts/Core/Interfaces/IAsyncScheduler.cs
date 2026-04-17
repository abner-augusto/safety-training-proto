using System.Threading;
using System.Threading.Tasks;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Async delay abstraction for Core logic that needs to wait without
    /// depending on UnityEngine.Awaitable. Unity implementation wraps
    /// <c>Awaitable.WaitForSecondsAsync</c>; harness implementation uses
    /// <c>Task.Delay</c>.
    /// </summary>
    public interface IAsyncScheduler
    {
        Task DelayAsync(float seconds, CancellationToken cancellationToken);
    }
}
