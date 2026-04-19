using System.Threading;
using SafetyProto.Core.Interfaces;
using UnityEngine;

namespace SafetyProto.Runtime.Task
{
    internal sealed class AwaitableAsyncSchedulerAdapter : IAsyncScheduler
    {
        public async System.Threading.Tasks.Task DelayAsync(float seconds, CancellationToken cancellationToken)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(seconds, cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
            }
        }
    }
}
