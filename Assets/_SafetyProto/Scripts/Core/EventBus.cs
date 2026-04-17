using System;
using System.Collections.Generic;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Gameplay.Events;
using UnityEngine;
using UnityEngine.Events;
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
            ClearStaticEvents();
        }

        private void OnDestroy()
        {
            ClearStaticEvents();
        }

        private static void ClearStaticEvents()
        {
            OnSessionStartedCSharp = null;
            OnSessionPausedCSharp = null;
            OnSessionResumedCSharp = null;
            OnSessionEndedCSharp = null;
            OnActionAttemptCSharp = null;
            OnPpeStateChangedCSharp = null;
            OnTaskStartedCSharp = null;
            OnTaskCompletedCSharp = null;
            OnTaskTimeoutCSharp = null;
            OnScoreChangedCSharp = null;
            OnGroupStartedCSharp = null;
            OnGroupCompletedCSharp = null;
            OnSafetyViolationCSharp = null;
            OnCriticalSafetyFailureCSharp = null;
            OnSafetyErrorCSharp = null;
        }

        private void Enqueue(Action action)
        {
            if (action == null) return;
            _eventQueue.Enqueue(action);
        }

        private const int MaxQueueWarningThreshold = 1000;

        public void ProcessEvents(double maxMillis = 2)
        {
            if (_eventQueue.Count == 0) return;

            if (_eventQueue.Count > MaxQueueWarningThreshold)
            {
                SafetyLog.Warning($"[EventBus] Queue length high: {_eventQueue.Count} items");
                SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                {
                    Source = "EventBus.Queue",
                    Message = "Event queue length exceeded threshold",
                    Details = _eventQueue.Count.ToString()
                });
            }

            _stopwatch.Restart();
            while (_eventQueue.Count > 0)
            {
                var action = _eventQueue.Dequeue();
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

            _stopwatch.Stop();
        }

        private void InvokeTyped<T>(T payload)
        {
            if (!_typedSubscribers.TryGetValue(typeof(T), out var raw)) return;
            if (raw is Action<T> handlers)
            {
                try { handlers.Invoke(payload); }
                catch (Exception ex)
                {
                    SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                    {
                        Source = "EventBus.InvokeTyped",
                        Message = "Typed subscriber threw",
                        Details = ex.ToString()
                    });
                }
            }
        }

        // ---------- IEventBus surface ----------
        //
        // Subscribers added via Subscribe<T> are invoked in addition to any UnityEvent
        // bindings. Dispatch is queued (via Enqueue) to preserve frame-boundary semantics
        // with the rest of the bus.

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
            switch (payload)
            {
                case SessionStartedEventArgs s:          RaiseSessionStarted(s); return;
                case SessionPausedEventArgs s:           RaiseSessionPaused(s); return;
                case SessionResumedEventArgs s:          RaiseSessionResumed(s); return;
                case SessionEndedEventArgs s:            RaiseSessionEnded(s); return;
                case SessionCompletedEventArgs s:        RaiseSessionCompleted(s); return;
                case ActionAttemptedEvent a:             RaiseActionAttempt(a); return;
                case PPEStateChangedEventArgs p:         RaisePpeStateChanged(p); return;
                case TaskEventArgs t:
                    switch (t.Phase)
                    {
                        case TaskPhase.Started:
                            RaiseTaskStarted(t);
                            return;
                        case TaskPhase.Timeout:
                            RaiseTaskTimeout(t);
                            return;
                        case TaskPhase.Completed:
                        default:
                            RaiseTaskCompleted(t);
                            return;
                    }
                case TaskGroupEventArgs g:
                    switch (g.Phase)
                    {
                        case TaskGroupPhase.Completed:
                            RaiseGroupCompleted(g);
                            return;
                        case TaskGroupPhase.Started:
                        default:
                            RaiseGroupStarted(g);
                            return;
                    }
                case ScoreChangedEventArgs sc:           RaiseScoreChanged(sc); return;
                case SafetyViolationEventArgs v:         RaiseSafetyViolation(v); return;
                case CriticalSafetyFailureEventArgs c:   RaiseCriticalSafetyFailure(c); return;
                case SafetyErrorEventArgs e:             RaiseSafetyError(e); return;
                default:
                    DispatchTyped(payload);
                    return;
            }
        }

        internal void DispatchTyped<T>(T payload)
        {
            if (!_typedSubscribers.TryGetValue(typeof(T), out var raw)) return;
            if (raw is Action<T> handlers)
            {
                Enqueue(() =>
                {
                    try { handlers.Invoke(payload); }
                    catch (Exception ex)
                    {
                        SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                        {
                            Source = "EventBus.DispatchTyped",
                            Message = "Typed subscriber threw",
                            Details = ex.ToString()
                        });
                    }
                });
            }
        }

        private static void StampMetadata(ref string sessionId, ref string playerId, ref string scenarioId, ref long timestampMs)
        {
            sessionId = EventContext.CurrentSessionId;
            playerId = EventContext.CurrentPlayerId;
            scenarioId = EventContext.CurrentScenarioId;
            timestampMs = EventContext.NowUnixMs();
        }

        [Header("Debug")]
        public bool verboseLogging;

        public static event Action<SessionStartedEventArgs> OnSessionStartedCSharp;
        public static event Action<SessionPausedEventArgs> OnSessionPausedCSharp;
        public static event Action<SessionResumedEventArgs> OnSessionResumedCSharp;
        public static event Action<SessionEndedEventArgs> OnSessionEndedCSharp;
        public static event Action<ActionAttemptedEvent> OnActionAttemptCSharp;
        public static event Action<PPEStateChangedEventArgs> OnPpeStateChangedCSharp;
        public static event Action<TaskEventArgs> OnTaskStartedCSharp;
        public static event Action<TaskEventArgs> OnTaskCompletedCSharp;
        public static event Action<TaskEventArgs> OnTaskTimeoutCSharp;
        public static event Action<ScoreChangedEventArgs> OnScoreChangedCSharp;
        public static event Action<TaskGroupEventArgs> OnGroupStartedCSharp;
        public static event Action<TaskGroupEventArgs> OnGroupCompletedCSharp;
        public static event Action<SafetyViolationEventArgs> OnSafetyViolationCSharp;
        public static event Action<CriticalSafetyFailureEventArgs> OnCriticalSafetyFailureCSharp;
        public static event Action<SafetyErrorEventArgs> OnSafetyErrorCSharp;

        [Header("Session Events")]
        public UnityEvent<SessionStartedEventArgs> onSessionStarted;
        public UnityEvent<SessionPausedEventArgs> onSessionPaused;
        public UnityEvent<SessionResumedEventArgs> onSessionResumed;
        public UnityEvent<SessionEndedEventArgs> onSessionEnded;

        [Header("Gameplay Events")]
        public UnityEvent<ActionAttemptedEvent> onActionAttempt;
        public UnityEvent<PPEStateChangedEventArgs> onPpeStateChanged;
        public UnityEvent<TaskEventArgs> onTaskStarted;
        public UnityEvent<TaskEventArgs> onTaskCompleted;
        public UnityEvent<TaskEventArgs> onTaskTimeout;
        public UnityEvent<ScoreChangedEventArgs> onScoreChanged;

        [Header("Group Events")]
        public UnityEvent<TaskGroupEventArgs> onGroupStarted;
        public UnityEvent<TaskGroupEventArgs> onGroupCompleted;

        [Header("End of Session")]
        public UnityEvent<SessionCompletedEventArgs> onSessionCompleted;

        [Header("Safety Events")]
        public UnityEvent<SafetyViolationEventArgs> onSafetyViolation;
        public UnityEvent<CriticalSafetyFailureEventArgs> onCriticalSafetyFailure;
        public UnityEvent<SafetyErrorEventArgs> onSafetyError;

        // --- Methods to Raise Events ---
        public void RaiseSessionStarted(SessionStartedEventArgs args = new SessionStartedEventArgs())
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info("[EventBus] SessionStarted");
                OnSessionStartedCSharp?.Invoke(payload);
                onSessionStarted?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseSessionPaused(SessionPausedEventArgs args = new SessionPausedEventArgs())
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info("[EventBus] SessionPaused");
                OnSessionPausedCSharp?.Invoke(payload);
                onSessionPaused?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseSessionResumed(SessionResumedEventArgs args = new SessionResumedEventArgs())
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info("[EventBus] SessionResumed");
                OnSessionResumedCSharp?.Invoke(payload);
                onSessionResumed?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseSessionEnded(SessionEndedEventArgs args = new SessionEndedEventArgs())
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info("[EventBus] SessionEnded");
                OnSessionEndedCSharp?.Invoke(payload);
                onSessionEnded?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseActionAttempt(ActionAttemptedEvent args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging)
                {
                    var positionText = payload.Position.HasValue ? payload.Position.Value.ToString() : "<none>";
                    SafetyLog.Info($"[EventBus] ActionAttempt: {payload.ActionId}, Interactor: {payload.InteractorId}, Pos: {positionText}");
                }
                OnActionAttemptCSharp?.Invoke(payload);
                onActionAttempt?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaisePpeStateChanged(PPEStateChangedEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info($"[EventBus] PPEStateChanged: {payload.PpeType}, Wearing: {payload.IsWearing}");
                OnPpeStateChangedCSharp?.Invoke(payload);
                onPpeStateChanged?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseTaskStarted(TaskEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            payload.Phase = TaskPhase.Started;
            Enqueue(() =>
            {
                if (verboseLogging && payload.Task != null) SafetyLog.Info($"[EventBus] TaskStarted: {payload.Task.taskName}");
                OnTaskStartedCSharp?.Invoke(payload);
                onTaskStarted?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseTaskCompleted(TaskEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            payload.Phase = TaskPhase.Completed;
            Enqueue(() =>
            {
                if (verboseLogging && payload.Task != null) SafetyLog.Info($"[EventBus] TaskCompleted: {payload.Task.taskName}");
                OnTaskCompletedCSharp?.Invoke(payload);
                onTaskCompleted?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseTaskTimeout(TaskEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            payload.Phase = TaskPhase.Timeout;
            Enqueue(() =>
            {
                if (verboseLogging && payload.Task != null) SafetyLog.Info($"[EventBus] TaskTimeout: {payload.Task.taskName}");
                OnTaskTimeoutCSharp?.Invoke(payload);
                onTaskTimeout?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseScoreChanged(ScoreChangedEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info($"[EventBus] ScoreChanged: Total {payload.TotalScore}, Delta {payload.Delta}");
                OnScoreChangedCSharp?.Invoke(payload);
                onScoreChanged?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseGroupStarted(TaskGroupEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            payload.Phase = TaskGroupPhase.Started;
            Enqueue(() =>
            {
                if (verboseLogging && payload.Group != null) SafetyLog.Info($"[EventBus] GroupStarted: {payload.Group.groupName}");
                OnGroupStartedCSharp?.Invoke(payload);
                onGroupStarted?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseGroupCompleted(TaskGroupEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            payload.Phase = TaskGroupPhase.Completed;
            Enqueue(() =>
            {
                if (verboseLogging && payload.Group != null) SafetyLog.Info($"[EventBus] GroupCompleted: {payload.Group.groupName}");
                OnGroupCompletedCSharp?.Invoke(payload);
                onGroupCompleted?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseSessionCompleted(SessionCompletedEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info($"[EventBus] SessionCompleted: {payload.tasksCompleted} tasks, {payload.totalElapsedTime:F2}s, Score: {payload.totalScore}");
                onSessionCompleted?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseSafetyViolation(SafetyViolationEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info($"[EventBus] SafetyViolation: {payload.ViolationCode} - {payload.Message}");
                OnSafetyViolationCSharp?.Invoke(payload);
                onSafetyViolation?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseCriticalSafetyFailure(CriticalSafetyFailureEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info($"[EventBus] CriticalSafetyFailure: {payload.Reason} ({payload.ViolationCount} in {payload.WindowSeconds}s)");
                OnCriticalSafetyFailureCSharp?.Invoke(payload);
                onCriticalSafetyFailure?.Invoke(payload);
                InvokeTyped(payload);
            });
        }

        public void RaiseSafetyError(SafetyErrorEventArgs args)
        {
            var payload = args;
            StampMetadata(ref payload.SessionId, ref payload.PlayerId, ref payload.ScenarioId, ref payload.TimestampMs);
            Enqueue(() =>
            {
                if (verboseLogging) SafetyLog.Info($"[EventBus] SafetyError: {payload.Source} - {payload.Message}");
                OnSafetyErrorCSharp?.Invoke(payload);
                onSafetyError?.Invoke(payload);
                InvokeTyped(payload);
            });
        }
    }
}
