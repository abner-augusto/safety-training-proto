using System;
using System.Collections.Generic;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Tests.Editor.Support
{
    public sealed class FakeEventBus : IEventBus
    {
        private readonly Dictionary<Type, Delegate> _subscribers = new Dictionary<Type, Delegate>();

        public List<(Type type, object payload)> PublishedEvents { get; } = new List<(Type, object)>();

        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            var key = typeof(T);
            _subscribers.TryGetValue(key, out var existing);
            _subscribers[key] = existing == null
                ? (Delegate)handler
                : Delegate.Combine(existing, handler);
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            var key = typeof(T);
            if (!_subscribers.TryGetValue(key, out var existing)) return;
            var remaining = Delegate.Remove(existing, handler);
            if (remaining == null) _subscribers.Remove(key);
            else _subscribers[key] = remaining;
        }

        public void Publish<T>(T payload)
        {
            PublishedEvents.Add((typeof(T), payload));
            if (_subscribers.TryGetValue(typeof(T), out var raw) && raw is Action<T> handlers)
            {
                handlers.Invoke(payload);
            }
        }

        public void Clear()
        {
            _subscribers.Clear();
            PublishedEvents.Clear();
        }
    }
}
