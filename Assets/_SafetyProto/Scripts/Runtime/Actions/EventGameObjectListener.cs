using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Gameplay.Events;
using SafetyProto.Utils;
using System.Collections.Generic;
using UnityEngine;
using SafetyProto.Core.Logging;
using UnityEngine.Serialization;

namespace SafetyProto.Gameplay.Actions
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
            TaskTimeout,
            ScoreChanged,
            PpeStateChanged,
            ActionAttempt,
            TasksCompleted,
        }

        [Header("Configuration")]
        public EventType eventTypeToListen;
        [Tooltip("Targets to enable/disable when the event fires.")]
        public GameObject[] targets;
        public bool enableOnEvent = true;

        private UnityEngine.Events.UnityAction<SessionStartedEventArgs>   _onSessionStarted;
        private UnityEngine.Events.UnityAction<SessionPausedEventArgs>    _onSessionPaused;
        private UnityEngine.Events.UnityAction<SessionResumedEventArgs>   _onSessionResumed;
        private UnityEngine.Events.UnityAction<SessionEndedEventArgs>     _onSessionEnded;
        private UnityEngine.Events.UnityAction<TaskEventArgs>             _onTaskStarted;
        private UnityEngine.Events.UnityAction<TaskEventArgs>             _onTaskCompleted;
        private UnityEngine.Events.UnityAction<TaskEventArgs>             _onTaskTimeout;
        private UnityEngine.Events.UnityAction<ScoreChangedEventArgs>     _onScoreChanged;
        private UnityEngine.Events.UnityAction<PPEStateChangedEventArgs>  _onPpeStateChanged;
        private UnityEngine.Events.UnityAction<ActionAttemptedEvent>      _onActionAttempt;
        private UnityEngine.Events.UnityAction<SessionCompletedEventArgs> _onTasksCompleted;

        // Back-compat for scenes/prefabs that had a single target field.
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
                    EventBus.Instance.onSessionStarted.AddListener(_onSessionStarted);
                    break;
                case EventType.SessionPaused:
                    _onSessionPaused = _ => Toggle();
                    EventBus.Instance.onSessionPaused.AddListener(_onSessionPaused);
                    break;
                case EventType.SessionResumed:
                    _onSessionResumed = _ => Toggle();
                    EventBus.Instance.onSessionResumed.AddListener(_onSessionResumed);
                    break;
                case EventType.SessionEnded:
                    _onSessionEnded = _ => Toggle();
                    EventBus.Instance.onSessionEnded.AddListener(_onSessionEnded);
                    break;
                case EventType.TaskStarted:
                    _onTaskStarted = _ => Toggle();
                    EventBus.Instance.onTaskStarted.AddListener(_onTaskStarted);
                    break;
                case EventType.TaskCompleted:
                    _onTaskCompleted = _ => Toggle();
                    EventBus.Instance.onTaskCompleted.AddListener(_onTaskCompleted);
                    break;
                case EventType.TaskTimeout:
                    _onTaskTimeout = _ => Toggle();
                    EventBus.Instance.onTaskTimeout.AddListener(_onTaskTimeout);
                    break;
                case EventType.ScoreChanged:
                    _onScoreChanged = _ => Toggle();
                    EventBus.Instance.onScoreChanged.AddListener(_onScoreChanged);
                    break;
                case EventType.PpeStateChanged:
                    _onPpeStateChanged = _ => Toggle();
                    EventBus.Instance.onPpeStateChanged.AddListener(_onPpeStateChanged);
                    break;
                case EventType.ActionAttempt:
                    _onActionAttempt = _ => Toggle();
                    EventBus.Instance.onActionAttempt.AddListener(_onActionAttempt);
                    break;
                case EventType.TasksCompleted:
                    _onTasksCompleted = _ => Toggle();
                    EventBus.Instance.onSessionCompleted.AddListener(_onTasksCompleted);
                    break;
            }
        }

        private void OnDestroy()
        {
            if (EventBus.Instance == null) return;
            if (_onSessionStarted  != null) EventBus.Instance.onSessionStarted.RemoveListener(_onSessionStarted);
            if (_onSessionPaused   != null) EventBus.Instance.onSessionPaused.RemoveListener(_onSessionPaused);
            if (_onSessionResumed  != null) EventBus.Instance.onSessionResumed.RemoveListener(_onSessionResumed);
            if (_onSessionEnded    != null) EventBus.Instance.onSessionEnded.RemoveListener(_onSessionEnded);
            if (_onTaskStarted     != null) EventBus.Instance.onTaskStarted.RemoveListener(_onTaskStarted);
            if (_onTaskCompleted   != null) EventBus.Instance.onTaskCompleted.RemoveListener(_onTaskCompleted);
            if (_onTaskTimeout     != null) EventBus.Instance.onTaskTimeout.RemoveListener(_onTaskTimeout);
            if (_onScoreChanged    != null) EventBus.Instance.onScoreChanged.RemoveListener(_onScoreChanged);
            if (_onPpeStateChanged != null) EventBus.Instance.onPpeStateChanged.RemoveListener(_onPpeStateChanged);
            if (_onActionAttempt   != null) EventBus.Instance.onActionAttempt.RemoveListener(_onActionAttempt);
            if (_onTasksCompleted  != null) EventBus.Instance.onSessionCompleted.RemoveListener(_onTasksCompleted);
        }

        private bool HasAnyTarget()
        {
            if (legacyTarget != null) return true;
            if (targets == null) return false;

            foreach (var t in targets)
                if (t != null) return true;

            return false;
        }

        private IEnumerable<GameObject> EnumerateTargets()
        {
            // Avoid duplicate SetActive calls (and tolerate null entries).
            var seen = new HashSet<GameObject>();

            // Runtime migration for builds where OnValidate doesn't run.
            if ((targets == null || targets.Length == 0) && legacyTarget != null)
                targets = new[] { legacyTarget };

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
