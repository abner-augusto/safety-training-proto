namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Observes each flush of the queued <see cref="EventBus"/>. Called on the main thread from
    /// inside <see cref="EventBus.ProcessEvents"/>, so an implementation must not allocate.
    /// </summary>
    public interface IEventBusFlushProbe
    {
        /// <param name="queueBefore">Queue length when the flush began.</param>
        /// <param name="dispatched">Events dispatched, including any enqueued during the flush.</param>
        /// <param name="elapsedTicks">Flush duration in <see cref="System.Diagnostics.Stopwatch"/> ticks.</param>
        /// <param name="cutByBudget">True when the time budget stopped the flush with events left
        /// for the next frame.</param>
        void OnFlush(int queueBefore, int dispatched, long elapsedTicks, bool cutByBudget);
    }
}
