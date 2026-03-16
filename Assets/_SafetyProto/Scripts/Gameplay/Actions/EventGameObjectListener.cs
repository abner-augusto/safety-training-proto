using SafetyProto.Core;
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

        // Back-compat for scenes/prefabs that had a single target field.
        [FormerlySerializedAs("target")]
        [SerializeField, HideInInspector] private GameObject legacyTarget;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Auto-migrate old data in the editor so users don't lose references when updating.
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
                    EventBus.Instance.onSessionStarted.AddListener(_ => Toggle());
                    break;
                case EventType.SessionPaused:
                    EventBus.Instance.onSessionPaused.AddListener(_ => Toggle());
                    break;
                case EventType.SessionResumed:
                    EventBus.Instance.onSessionResumed.AddListener(_ => Toggle());
                    break;
                case EventType.SessionEnded:
                    EventBus.Instance.onSessionEnded.AddListener(_ => Toggle());
                    break;
                case EventType.TaskStarted:
                    EventBus.Instance.onTaskStarted.AddListener(_ => Toggle());
                    break;
                case EventType.TaskCompleted:
                    EventBus.Instance.onTaskCompleted.AddListener(_ => Toggle());
                    break;
                case EventType.TaskTimeout:
                    EventBus.Instance.onTaskTimeout.AddListener(_ => Toggle());
                    break;
                case EventType.ScoreChanged:
                    EventBus.Instance.onScoreChanged.AddListener(_ => Toggle());
                    break;
                case EventType.PpeStateChanged:
                    EventBus.Instance.onPpeStateChanged.AddListener(_ => Toggle());
                    break;
                case EventType.ActionAttempt:
                    EventBus.Instance.onActionAttempt.AddListener(_ => Toggle());
                    break;
                case EventType.TasksCompleted:
                    EventBus.Instance.onSessionCompleted.AddListener(_ => Toggle());
                    break;
            }
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
