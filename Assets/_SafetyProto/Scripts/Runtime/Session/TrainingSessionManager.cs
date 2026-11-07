using System;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Sessions;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace SafetyProto.Runtime.Session
{
    public class TrainingSessionManager : MonoBehaviour, ISessionResettable
    {
        [Tooltip("Start the session automatically on scene load. Disable when a pre-session flow " +
                 "(e.g. NameEntryController) drives the start after capturing the participant id.")]
        [SerializeField] private bool autoStartOnStart = true;

        private bool _appPaused;
        private bool _appFocused = true;
        private bool _xrSessionWasFocused;
        private bool _holdingAppPause;
        private bool _holdingAppFocus;
        private bool _holdingXrFocus;
        private bool _sessionStarted;
        private bool _sessionEnded;
        private bool _sessionCompleted;
        private int _attempt;
        private string _previousSessionId = string.Empty;

        public bool IsSessionStarted => _sessionStarted;
        /// <summary>The session reached SessionCompleted (its log is final).</summary>
        public bool IsSessionCompleted => _sessionCompleted;
        /// <summary>1 for a first attempt, +1 per interrupted session this one restarts; 0 before start.</summary>
        public int Attempt => _attempt;
        public string PreviousSessionId => _previousSessionId;

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            EventBus.Instance.Subscribe<SessionEndedEventArgs>(OnSessionEnded);
            EventBus.Instance.Subscribe<SessionCompletedEventArgs>(OnSessionCompleted);

            if (autoStartOnStart)
            {
                BeginSession();
            }
        }

        private void OnSessionEnded(SessionEndedEventArgs _)
        {
            _sessionEnded = true;
        }

        private void OnSessionCompleted(SessionCompletedEventArgs _)
        {
            _sessionCompleted = true;
        }

        /// <summary>
        /// Starts the training session: resets scoring, stamps the EventContext with the current
        /// participant id (from <see cref="ParticipantIdentity"/>, falling back to "Player1"), and
        /// raises SessionStarted. Idempotent — safe to call once after the participant id is set.
        /// </summary>
        public void BeginSession() => BeginSession(string.Empty, 1);

        /// <summary>Starts a session that restarts <paramref name="previousSessionId"/> (empty for a
        /// first attempt); <paramref name="attempt"/> counts the tries of the same participant.</summary>
        public void BeginSession(string previousSessionId, int attempt)
        {
            if (_sessionStarted)
            {
                return;
            }
            _sessionStarted = true;
            _previousSessionId = previousSessionId ?? string.Empty;
            _attempt = attempt < 1 ? 1 : attempt;

            ScoreService.Instance.ResetSession();

            string playerId = string.IsNullOrEmpty(ParticipantIdentity.CurrentId)
                ? "Player1"
                : ParticipantIdentity.CurrentId;

            EventContext.StartSession(
                Guid.NewGuid().ToString(),
                playerId,
                SceneManager.GetActiveScene().name);

            int totalTasks = TaskManager.Instance != null ? TaskManager.Instance.TotalTaskCount : 0;
            SessionEvents.RaiseSessionStarted(new SessionStartedEventArgs
            {
                TotalTasks = totalTasks,
                ParticipantSource = ParticipantIdentity.CurrentSource,
                PreviousSessionId = _previousSessionId,
                Attempt = _attempt,
                HandCollision = SafetyProto.Runtime.Interaction.HandObjectCollision.Instance != null
                    ? SafetyProto.Runtime.Interaction.HandObjectCollision.Token(SafetyProto.Runtime.Interaction.HandObjectCollision.Instance.Current)
                    : string.Empty
            });
            SafetyLog.Info($"TrainingSessionManager: Session Started event raised (participante {playerId}).", this);
        }

        private void Update() => RefreshFocusHold();

        private void OnApplicationPause(bool pauseStatus)
        {
            _appPaused = pauseStatus;
            RefreshFocusHold();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _appFocused = hasFocus;
            RefreshFocusHold();
        }

        /// <summary>
        /// Each focus signal holds the pause as its own source, so the log can tell a system
        /// menu (XR session unfocused only) from a headset that went to sleep (activity paused).
        /// The session is paused exactly when any of the three is lost, as with a single hold.
        /// </summary>
        private void RefreshFocusHold()
        {
            SetFocusHold(PauseSource.ApplicationPause, _appPaused, ref _holdingAppPause);
            SetFocusHold(PauseSource.ApplicationFocus, !_appFocused, ref _holdingAppFocus);
            SetFocusHold(PauseSource.XrSessionUnfocused, IsXrSessionUnfocused(), ref _holdingXrFocus);
        }

        private void SetFocusHold(PauseSource source, bool lost, ref bool holding)
        {
            if (lost == holding)
            {
                return;
            }
            holding = lost;

            if (lost)
            {
                SessionPause.Hold(source);
                SafetyLog.Info($"TrainingSessionManager: focus lost ({source}), session pause held.", this);
            }
            else
            {
                SessionPause.Release(source);
                SafetyLog.Info($"TrainingSessionManager: focus regained ({source}), session pause released.", this);
            }
        }

        /// <summary>
        /// OpenXR never reports session focus through OnApplicationFocus/OnApplicationPause:
        /// taking the headset off or opening the system menu leaves the XR session unfocused
        /// while the app keeps running frames, and the Activity only pauses once the headset
        /// sleeps. So the XR session state has to be polled alongside the app callbacks.
        /// </summary>
        private bool IsXrSessionUnfocused()
        {
            if (!XRSettings.isDeviceActive)
            {
                return false;
            }

            // OpenXR reaches FOCUSED only some frames after startup.
            bool xrFocused = OpenXRUtility.IsSessionFocused;
            _xrSessionWasFocused |= xrFocused;
            return !xrFocused && _xrSessionWasFocused;
        }

        /// <summary>
        /// Every reset (dashboard or in-VR menu) passes here before the scene reloads: hand the
        /// session over so the next scene restarts the same participant when this one was
        /// interrupted, or waits for the next participant when it was completed.
        /// </summary>
        public void ResetSession()
        {
            var snapshot = new SessionSnapshot
            {
                Started = _sessionStarted,
                Completed = _sessionCompleted,
                SessionId = EventContext.CurrentSessionId ?? string.Empty,
                ParticipantId = EventContext.CurrentPlayerId ?? string.Empty,
                Source = ParticipantIdentity.CurrentSource,
                Mode = SessionModeState.Current,
                Attempt = _attempt
            };
            if (!SessionHandoff.HandOver(snapshot))
                ParticipantIdentity.Clear();
        }

        private void RaiseAbortedSessionEnd()
        {
            SessionEvents.RaiseSessionEnded();
            SafetyLog.Info("TrainingSessionManager: Session Ended event raised.", this);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.Unsubscribe<SessionEndedEventArgs>(OnSessionEnded);
                EventBus.Instance.Unsubscribe<SessionCompletedEventArgs>(OnSessionCompleted);
                if (!_sessionEnded)
                    RaiseAbortedSessionEnd();
            }

            ScoreService.DestroyInstance();
            EventContext.Clear();
            SessionPause.Reset();
        }
    }
}
