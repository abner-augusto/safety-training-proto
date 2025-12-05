using SafetyProto.Core;
using SafetyProto.Utils;
using UnityEngine;
using SafetyProto.Core.Logging;

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
        public GameObject target;
        public bool enableOnEvent = true;

        private void Start()
        {
            if (target == null)
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

        private void Toggle()
        {
            target.SetActive(enableOnEvent);
        }
    }
}
