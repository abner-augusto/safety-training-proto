using System;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Engine-independent pub/sub contract.
    /// Unity and CLI harness provide separate implementations.
    ///
    /// Semantics:
    /// - Subscription order determines invocation order (no priorities).
    /// - Exceptions raised by a handler must not prevent subsequent handlers from running.
    /// - Dispatch timing is implementation-defined (Unity: queued, flushed each frame;
    ///   harness: synchronous).
    /// </summary>
    public interface IEventBus
    {
        void Subscribe<T>(Action<T> handler);
        void Unsubscribe<T>(Action<T> handler);
        void Publish<T>(T payload);
    }
}
