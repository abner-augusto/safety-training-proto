using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Utils;
using System.Collections.Generic;
using UnityEngine;
using SafetyProto.Core.Logging;
using UnityEngine.Serialization;

namespace SafetyProto.Runtime.Actions
{
    public class EventGameObjectToggleListener : MonoBehaviour
    {
        public enum EventType
        {
            SessionStarted,
            SessionPaused,
            SessionResumed,
            SessionEnded,
            TaskStarted,
            TaskCompleted,
            ScoreChanged,
            PpeStateChanged,
            ActionAttempt,
            TasksCompleted,
            ConsequenceStarted,
            ConsequenceEnded,
        }

        [Header("Configuration")]
        public EventType eventTypeToListen;
        [Tooltip("Targets to enable/disable when the event fires.")]
        public GameObject[] targets;
        public bool enableOnEvent = true;

        private System.Action<SessionStartedEventArgs>   _onSessionStarted;
        private System.Action<SessionPausedEventArgs>    _onSessionPaused;
        private System.Action<SessionResumedEventArgs>   _onSessionResumed;
        private System.Action<SessionEndedEventArgs>     _onSessionEnded;
        private System.Action<TaskEventArgs>             _onTaskStarted;
        private System.Action<TaskEventArgs>             _onTaskCompleted;
        private System.Action<ScoreChangedEventArgs>     _onScoreChanged;
        private System.Action<PPEStateChangedEventArgs>  _onPpeStateChanged;
        private System.Action<ActionAttemptedEvent>      _onActionAttempt;
        private System.Action<SessionCompletedEventArgs> _onTasksCompleted;
        private System.Action<ConsequenceStartedEventArgs> _onConsequenceStarted;
        private System.Action _onConsequenceEnded;

        [FormerlySerializedAs("target")]
        [SerializeField, HideInInspector] private GameObject legacyTarget;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (legacyTarget != null)
            {
                if (targets == null || targets.Length == 0)
                {
                    targets = new[] { legacyTarget };
                }
                else
                {
                    bool contains = false;
                    foreach (var t in targets)
                    {
                        if (t == legacyTarget) { contains = true; break; }
                    }

                    if (!contains)
                    {
                        var newTargets = new GameObject[targets.Length + 1];
                        targets.CopyTo(newTargets, 0);
                        newTargets[newTargets.Length - 1] = legacyTarget;
                        targets = newTargets;
                    }
                }

                legacyTarget = null;
            }
        }
#endif

        private void Start()
        {
            if (!HasAnyTarget())
            {
                SafetyLog.Error("Missing target reference in EventGameObjectToggleListener", this);
                enabled = false;
                return;
            }

            if (!this.IsEventBusReady())
            {
                return;
            }

            switch (eventTypeToListen)
            {
                case EventType.SessionStarted:
                    _onSessionStarted = _ => Toggle();
                    EventBus.Instance.Subscribe<SessionStartedEventArgs>(_onSessionStarted);
                    break;
                case EventType.SessionPaused:
                    _onSessionPaused = _ => Toggle();
                    EventBus.Instance.Subscribe<SessionPausedEventArgs>(_onSessionPaused);
                    break;
                case EventType.SessionResumed:
                    _onSessionResumed = _ => Toggle();
                    EventBus.Instance.Subscribe<SessionResumedEventArgs>(_onSessionResumed);
                    break;
                case EventType.SessionEnded:
                    _onSessionEnded = _ => Toggle();
                    EventBus.Instance.Subscribe<SessionEndedEventArgs>(_onSessionEnded);
                    break;
                case EventType.TaskStarted:
                    _onTaskStarted = t => { if (t.Phase == TaskPhase.Started) Toggle(); };
                    EventBus.Instance.Subscribe<TaskEventArgs>(_onTaskStarted);
                    break;
                case EventType.TaskCompleted:
                    _onTaskCompleted = t => { if (t.Phase == TaskPhase.Completed) Toggle(); };
                    EventBus.Instance.Subscribe<TaskEventArgs>(_onTaskCompleted);
                    break;
                case EventType.ScoreChanged:
                    _onScoreChanged = _ => Toggle();
                    EventBus.Instance.Subscribe<ScoreChangedEventArgs>(_onScoreChanged);
                    break;
                case EventType.PpeStateChanged:
                    _onPpeStateChanged = _ => Toggle();
                    EventBus.Instance.Subscribe<PPEStateChangedEventArgs>(_onPpeStateChanged);
                    break;
                case EventType.ActionAttempt:
                    _onActionAttempt = _ => Toggle();
                    EventBus.Instance.Subscribe<ActionAttemptedEvent>(_onActionAttempt);
                    break;
                case EventType.TasksCompleted:
                    _onTasksCompleted = _ => Toggle();
                    EventBus.Instance.Subscribe<SessionCompletedEventArgs>(_onTasksCompleted);
                    break;
                case EventType.ConsequenceStarted:
                    _onConsequenceStarted = _ => Toggle();
                    DirectConsequenceChannel.OnConsequenceStarted += _onConsequenceStarted;
                    break;
                case EventType.ConsequenceEnded:
                    _onConsequenceEnded = Toggle;
                    DirectConsequenceChannel.OnConsequenceEnded += _onConsequenceEnded;
                    break;
            }
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                if (_onSessionStarted  != null) EventBus.Instance.Unsubscribe<SessionStartedEventArgs>(_onSessionStarted);
                if (_onSessionPaused   != null) EventBus.Instance.Unsubscribe<SessionPausedEventArgs>(_onSessionPaused);
                if (_onSessionResumed  != null) EventBus.Instance.Unsubscribe<SessionResumedEventArgs>(_onSessionResumed);
                if (_onSessionEnded    != null) EventBus.Instance.Unsubscribe<SessionEndedEventArgs>(_onSessionEnded);
                if (_onTaskStarted     != null) EventBus.Instance.Unsubscribe<TaskEventArgs>(_onTaskStarted);
                if (_onTaskCompleted   != null) EventBus.Instance.Unsubscribe<TaskEventArgs>(_onTaskCompleted);
                if (_onScoreChanged    != null) EventBus.Instance.Unsubscribe<ScoreChangedEventArgs>(_onScoreChanged);
                if (_onPpeStateChanged != null) EventBus.Instance.Unsubscribe<PPEStateChangedEventArgs>(_onPpeStateChanged);
                if (_onActionAttempt   != null) EventBus.Instance.Unsubscribe<ActionAttemptedEvent>(_onActionAttempt);
                if (_onTasksCompleted  != null) EventBus.Instance.Unsubscribe<SessionCompletedEventArgs>(_onTasksCompleted);
            }
            if (_onConsequenceStarted != null) DirectConsequenceChannel.OnConsequenceStarted -= _onConsequenceStarted;
            if (_onConsequenceEnded != null) DirectConsequenceChannel.OnConsequenceEnded -= _onConsequenceEnded;
        }

        private bool HasAnyTarget()
        {
            if (legacyTarget != null) return true;
            if (targets == null) return false;

            foreach (var t in targets)
                if (t != null) return true;

            return false;
        }

        private void MigrateLegacyTarget()
        {
            if ((targets == null || targets.Length == 0) && legacyTarget != null)
                targets = new[] { legacyTarget };
        }

        private IEnumerable<GameObject> EnumerateTargets()
        {
            var seen = new HashSet<GameObject>();

            MigrateLegacyTarget();

            if (targets == null) yield break;

            foreach (var t in targets)
            {
                if (t != null && seen.Add(t))
                    yield return t;
            }
        }

        private void Toggle()
        {
            foreach (var t in EnumerateTargets())
                t.SetActive(enableOnEvent);
        }
    }
}
