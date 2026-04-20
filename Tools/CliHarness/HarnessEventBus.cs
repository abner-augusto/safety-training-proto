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
        payload = StampMetadata(payload);

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

    private static T StampMetadata<T>(T payload)
    {
        object? boxed = payload;
        if (boxed == null) return payload;

        switch (boxed)
        {
            case SessionStartedEventArgs s:
                s.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                s.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                s.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                s.TimestampMs = EventContext.NowUnixMs();
                boxed = s;
                break;
            case SessionPausedEventArgs sp:
                sp.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                sp.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                sp.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                sp.TimestampMs = EventContext.NowUnixMs();
                boxed = sp;
                break;
            case SessionResumedEventArgs sr:
                sr.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                sr.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                sr.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                sr.TimestampMs = EventContext.NowUnixMs();
                boxed = sr;
                break;
            case SessionCompletedEventArgs sc:
                sc.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                sc.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                sc.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                sc.TimestampMs = EventContext.NowUnixMs();
                boxed = sc;
                break;
            case ActionAttemptedEvent a:
                a.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                a.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                a.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                a.TimestampMs = EventContext.NowUnixMs();
                boxed = a;
                break;
            case PPEStateChangedEventArgs p:
                p.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                p.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                p.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                p.TimestampMs = EventContext.NowUnixMs();
                boxed = p;
                break;
            case TaskEventArgs t:
                t.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                t.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                t.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                t.TimestampMs = EventContext.NowUnixMs();
                boxed = t;
                break;
            case TaskGroupEventArgs g:
                g.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                g.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                g.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                g.TimestampMs = EventContext.NowUnixMs();
                boxed = g;
                break;
            case ScoreChangedEventArgs sch:
                sch.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                sch.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                sch.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                sch.TimestampMs = EventContext.NowUnixMs();
                boxed = sch;
                break;
            case SafetyViolationEventArgs v:
                v.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                v.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                v.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                v.TimestampMs = EventContext.NowUnixMs();
                boxed = v;
                break;
            case SafetyErrorEventArgs se:
                se.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                se.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                se.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                se.TimestampMs = EventContext.NowUnixMs();
                boxed = se;
                break;
            case CriticalSafetyFailureEventArgs cf:
                cf.SessionId = EventContext.CurrentSessionId ?? string.Empty;
                cf.PlayerId = EventContext.CurrentPlayerId ?? string.Empty;
                cf.ScenarioId = EventContext.CurrentScenarioId ?? string.Empty;
                cf.TimestampMs = EventContext.NowUnixMs();
                boxed = cf;
                break;
        }

        return (T)boxed;
    }
}
