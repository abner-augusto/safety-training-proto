using System;
using System.Collections.Generic;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SafetyProto.Core
{
    [CreateAssetMenu(fileName = "EventBus", menuName = "VRSafetyTraining/EventBus", order = 0)]
    public class EventBus : ScriptableObject, IEventBus
    {
        private static EventBus _instance;
        private readonly Queue<Action> _eventQueue = new Queue<Action>();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly Dictionary<Type, Delegate> _typedSubscribers = new Dictionary<Type, Delegate>();

        public static EventBus Instance
        {
            get
            {
                EnsureLoaded();
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Preload() => EnsureLoaded();

        private static void EnsureLoaded()
        {
            if (_instance != null)
            {
                return;
            }

            _instance = Resources.Load<EventBus>(GameConstants.ResourcePaths.EventBus);
            if (_instance == null)
            {
                SafetyLog.Error(
                    $"[EventBus] Instance not found at Resources/{GameConstants.ResourcePaths.EventBus}. " +
                    $"Ensure a single EventBus.asset exists under a Resources folder. ({GameConstants.Logging.ProjectIdentifier})");
            }
        }

        private void OnEnable()
        {
            _instance = this;
        }

        private void OnDisable()
        {
            _instance = null;
        }

        private void Enqueue(Action action)
        {
            if (action == null) return;
            _eventQueue.Enqueue(action);
        }

        private const int MaxQueueWarningThreshold = 1000;
        private bool _queueOverflowReported;

        public void ProcessEvents(double maxMillis = 2)
        {
            if (_eventQueue.Count == 0) return;

            if (_eventQueue.Count > MaxQueueWarningThreshold && !_queueOverflowReported)
            {
                _queueOverflowReported = true;
                SafetyLog.Warning($"[EventBus] Queue length high: {_eventQueue.Count} items");
                SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                {
                    Source = "EventBus.Queue",
                    Message = "Event queue length exceeded threshold",
                    Details = _eventQueue.Count.ToString()
                });
            }

            int queueBefore = _eventQueue.Count;
            int dispatched = 0;
            _stopwatch.Restart();
            while (_eventQueue.Count > 0)
            {
                var action = _eventQueue.Dequeue();
                dispatched++;
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                    {
                        Source = "EventBus.ProcessEvents",
                        Message = "Exception during event dispatch",
                        Details = ex.ToString()
                    });
                }

                if (_stopwatch.Elapsed.TotalMilliseconds >= maxMillis) break;
            }

            if (_eventQueue.Count == 0) _queueOverflowReported = false;
            _stopwatch.Stop();

            FlushProbe?.OnFlush(queueBefore, dispatched, _stopwatch.ElapsedTicks, _eventQueue.Count > 0);
        }

        /// <summary>Receives one call per non-empty <see cref="ProcessEvents"/>, for on-device
        /// timing of the queued dispatch. Null (the default) costs one null check per flush.</summary>
        public static IEventBusFlushProbe FlushProbe { get; set; }

        // Domain reload is disabled on Play Mode entry; static state persists between runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFlushProbe() => FlushProbe = null;

        private void InvokeTyped<T>(T payload, string source)
        {
            if (!_typedSubscribers.TryGetValue(typeof(T), out var raw)) return;
            if (raw is Action<T> handlers)
            {
                try { handlers.Invoke(payload); }
                catch (Exception ex)
                {
                    ReportListenerError(source, ex, payload is not SafetyErrorEventArgs);
                }
            }
        }

        private static void ReportListenerError(string source, Exception ex, bool raiseSafetyError)
        {
            SafetyLog.Error($"[EventBus] Listener threw during {source}: {ex}");
            if (raiseSafetyError)
            {
                SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                {
                    Source = source,
                    Message = "Event listener threw",
                    Details = ex.ToString()
                });
            }
        }

        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            var key = typeof(T);
            _typedSubscribers.TryGetValue(key, out var existing);
            _typedSubscribers[key] = existing == null
                ? (Delegate)handler
                : Delegate.Combine(existing, handler);
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            var key = typeof(T);
            if (!_typedSubscribers.TryGetValue(key, out var existing)) return;
            var remaining = Delegate.Remove(existing, handler);
            if (remaining == null) _typedSubscribers.Remove(key);
            else _typedSubscribers[key] = remaining;
        }

        public void Publish<T>(T payload)
        {
            var stamped = EventMetadata.Stamp(payload);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info($"[EventBus] {typeof(T).Name}");
                InvokeTyped(stamped, "EventBus." + typeof(T).Name);
            });
        }

        [Header("Debug")]
        public bool verboseLogging;
    }
}
