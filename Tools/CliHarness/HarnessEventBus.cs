using System;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.CliHarness;

public sealed class HarnessEventBus : IEventBus
{
    private readonly Dictionary<Type, Delegate> _subscribers = new();
    private readonly Queue<Action> _queue = new();
    private bool _draining;

    public void Subscribe<T>(Action<T> handler)
    {
        if (handler == null) return;
        _subscribers.TryGetValue(typeof(T), out var existing);
        _subscribers[typeof(T)] = existing == null
            ? (Delegate)handler
            : Delegate.Combine(existing, handler);
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        if (handler == null) return;
        if (!_subscribers.TryGetValue(typeof(T), out var existing)) return;
        var remaining = Delegate.Remove(existing, handler);
        if (remaining == null) _subscribers.Remove(typeof(T));
        else _subscribers[typeof(T)] = remaining;
    }

    public void Publish<T>(T payload)
    {
        payload = EventMetadata.Stamp(payload);

        _queue.Enqueue(() =>
        {
            if (_subscribers.TryGetValue(typeof(T), out var raw) && raw is Action<T> handlers)
            {
                handlers.Invoke(payload);
            }
        });

        if (!_draining)
        {
            Drain();
        }
    }

    private void Drain()
    {
        _draining = true;
        try
        {
            while (_queue.Count > 0)
            {
                var action = _queue.Dequeue();
                action.Invoke();
            }
        }
        finally
        {
            _draining = false;
        }
    }

}
