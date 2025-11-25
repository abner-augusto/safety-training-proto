using System;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Core
{
    // The CreateAssetMenu attribute is still useful for initial creation,
    // but the singleton pattern will enforce a single instance loaded from Resources.
    [CreateAssetMenu(fileName = "EventBus", menuName = "VRSafetyTraining/EventBus", order = 0)]
    public class EventBus : ScriptableObject
    {
        private static EventBus _instance;

        public static EventBus Instance
        {
            get
            {
                EnsureLoaded();
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Preload()
        {
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_instance != null)
            {
                return;
            }

            _instance = Resources.Load<EventBus>(GameConstants.ResourcePaths.EventBus);
            if (_instance == null)
            {
                Debug.LogError(
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
        }

        [Header("Debug")]
        public bool verboseLogging;

        // --- C# Events (optional, but good for non-MonoBehaviour systems) ---
        public static event Action<SessionStartedEventArgs> OnSessionStartedCSharp;
        public static event Action<SessionPausedEventArgs> OnSessionPausedCSharp;
        public static event Action<SessionResumedEventArgs> OnSessionResumedCSharp;
        public static event Action<SessionEndedEventArgs> OnSessionEndedCSharp;
        public static event Action<ActionAttemptEventArgs> OnActionAttemptCSharp;
        public static event Action<PPEStateChangedEventArgs> OnPpeStateChangedCSharp;
        public static event Action<TaskEventArgs> OnTaskStartedCSharp;
        public static event Action<TaskEventArgs> OnTaskCompletedCSharp;
        public static event Action<TaskEventArgs> OnTaskTimeoutCSharp;
        public static event Action<ScoreChangedEventArgs> OnScoreChangedCSharp;
        public static event Action<TaskGroupEventArgs> OnGroupStartedCSharp;
        public static event Action<TaskGroupEventArgs> OnGroupCompletedCSharp;

        // --- UnityEvents (for Inspector assignment) ---
        [Header("Session Events")]
        public UnityEvent<SessionStartedEventArgs> onSessionStarted;
        public UnityEvent<SessionPausedEventArgs> onSessionPaused;
        public UnityEvent<SessionResumedEventArgs> onSessionResumed;
        public UnityEvent<SessionEndedEventArgs> onSessionEnded;

        [Header("Gameplay Events")]
        public UnityEvent<ActionAttemptEventArgs> onActionAttempt;
        public UnityEvent<PPEStateChangedEventArgs> onPpeStateChanged;
        public UnityEvent<TaskEventArgs> onTaskStarted;
        public UnityEvent<TaskEventArgs> onTaskCompleted;
        public UnityEvent<TaskEventArgs> onTaskTimeout; // TaskEventArgs will contain the timed-out task
        public UnityEvent<ScoreChangedEventArgs> onScoreChanged;

        [Header("Group Events")] // NEW
        public UnityEvent<TaskGroupEventArgs> onGroupStarted;
        public UnityEvent<TaskGroupEventArgs> onGroupCompleted;

        [Header("End of Session")]
        public UnityEvent<SessionCompletedEventArgs> onSessionCompleted;

        // --- Methods to Raise Events ---
        public void RaiseSessionStarted(SessionStartedEventArgs args = new SessionStartedEventArgs())
        {
            if (verboseLogging) Debug.Log("[EventBus] SessionStarted");
            OnSessionStartedCSharp?.Invoke(args);
            onSessionStarted?.Invoke(args);
        }

        public void RaiseSessionPaused(SessionPausedEventArgs args = new SessionPausedEventArgs())
        {
            if (verboseLogging) Debug.Log("[EventBus] SessionPaused");
            OnSessionPausedCSharp?.Invoke(args);
            onSessionPaused?.Invoke(args);
        }

        public void RaiseSessionResumed(SessionResumedEventArgs args = new SessionResumedEventArgs())
        {
            if (verboseLogging) Debug.Log("[EventBus] SessionResumed");
            OnSessionResumedCSharp?.Invoke(args);
            onSessionResumed?.Invoke(args);
        }

        public void RaiseSessionEnded(SessionEndedEventArgs args = new SessionEndedEventArgs())
        {
            if (verboseLogging) Debug.Log("[EventBus] SessionEnded");
            OnSessionEndedCSharp?.Invoke(args);
            onSessionEnded?.Invoke(args);
        }

        public void RaiseActionAttempt(ActionAttemptEventArgs args)
        {
            if (verboseLogging) Debug.Log($"[EventBus] ActionAttempt: {args.ActionType}, Interactor: {args.InteractorId}, Pos: {args.WorldPosition}");
            OnActionAttemptCSharp?.Invoke(args);
            onActionAttempt?.Invoke(args);
        }

        public void RaisePpeStateChanged(PPEStateChangedEventArgs args)
        {
            if (verboseLogging) Debug.Log($"[EventBus] PPEStateChanged: {args.PpeType}, Wearing: {args.IsWearing}");
            OnPpeStateChangedCSharp?.Invoke(args);
            onPpeStateChanged?.Invoke(args);
        }

        public void RaiseTaskStarted(TaskEventArgs args)
        {
            if (verboseLogging && args.Task != null) Debug.Log($"[EventBus] TaskStarted: {args.Task.taskName}");
            OnTaskStartedCSharp?.Invoke(args);
            onTaskStarted?.Invoke(args);
        }

        public void RaiseTaskCompleted(TaskEventArgs args)
        {
            if (verboseLogging && args.Task != null) Debug.Log($"[EventBus] TaskCompleted: {args.Task.taskName}");
            OnTaskCompletedCSharp?.Invoke(args);
            onTaskCompleted?.Invoke(args);
        }

        public void RaiseTaskTimeout(TaskEventArgs args)
        {
            if (verboseLogging && args.Task != null) Debug.Log($"[EventBus] TaskTimeout: {args.Task.taskName}");
            OnTaskTimeoutCSharp?.Invoke(args);
            onTaskTimeout?.Invoke(args);
        }

        public void RaiseScoreChanged(ScoreChangedEventArgs args)
        {
            if (verboseLogging) Debug.Log($"[EventBus] ScoreChanged: Total {args.TotalScore}, Delta {args.Delta}");
            OnScoreChangedCSharp?.Invoke(args);
            onScoreChanged?.Invoke(args);
        }

        public void RaiseGroupStarted(TaskGroupEventArgs args)
        {
            if (verboseLogging && args.Group != null) Debug.Log($"[EventBus] GroupStarted: {args.Group.groupName}");
            OnGroupStartedCSharp?.Invoke(args);
            onGroupStarted?.Invoke(args);
        }

        public void RaiseGroupCompleted(TaskGroupEventArgs args)
        {
            if (verboseLogging && args.Group != null) Debug.Log($"[EventBus] GroupCompleted: {args.Group.groupName}");
            OnGroupCompletedCSharp?.Invoke(args);
            onGroupCompleted?.Invoke(args);
        }

        public void RaiseSessionCompleted(SessionCompletedEventArgs args)
        {
            if (verboseLogging) Debug.Log($"[EventBus] SessionCompleted: {args.tasksCompleted} tasks, {args.totalElapsedTime:F2}s, Score: {args.totalScore}");
            onSessionCompleted?.Invoke(args);
        }
    }
}
