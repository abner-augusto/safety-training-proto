using System.Threading;
using SafetyProto.Core.Interfaces;
using UnityEngine;

namespace SafetyProto.Runtime.Task
{
    internal sealed class AwaitableAsyncSchedulerAdapter : IAsyncScheduler
    {
        public System.Threading.Tasks.Task DelayAsync(float seconds, CancellationToken cancellationToken)
            => Await(seconds, cancellationToken);

        private static async System.Threading.Tasks.Task Await(float seconds, CancellationToken cancellationToken)
        {
            await Awaitable.WaitForSecondsAsync(seconds, cancellationToken);
        }
    }
}
