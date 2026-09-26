using System;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace SafetyProto.Runtime.Session
{
    public class TrainingSessionManager : MonoBehaviour
    {
        [Tooltip("Start the session automatically on scene load. Disable when a pre-session flow " +
                 "(e.g. NameEntryController) drives the start after capturing the participant id.")]
        [SerializeField] private bool autoStartOnStart = true;

        private bool _appPaused;
        private bool _appFocused = true;
        private bool _xrSessionWasFocused;
        private bool _focusLost;
        private bool _sessionStarted;
        private bool _sessionEnded;

        public bool IsSessionStarted => _sessionStarted;

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            // Observe the domain terminal signal so OnDestroy doesn't re-raise SessionEnded
            // for a session that already ended logically (TaskManagerCore.EndSession publishes
            // SessionEnded on normal completion or a group timeout). We only raise from
            // OnDestroy for the abort case: app quit / scene unload mid-session, before the
            // session reached its logical end.
            EventBus.Instance.onSessionEnded.AddListener(OnSessionEnded);

            if (autoStartOnStart)
            {
                BeginSession();
            }
        }

        private void OnSessionEnded(SessionEndedEventArgs _)
        {
            _sessionEnded = true;
        }

        /// <summary>
        /// Starts the training session: resets scoring, stamps the EventContext with the current
        /// participant id (from <see cref="ParticipantIdentity"/>, falling back to "Player1"), and
        /// raises SessionStarted. Idempotent — safe to call once after the participant id is set.
        /// </summary>
        public void BeginSession()
        {
            if (_sessionStarted)
            {
                return;
            }
            _sessionStarted = true;

            ScoreService.Instance.ResetSession();

            string playerId = string.IsNullOrEmpty(ParticipantIdentity.CurrentId)
                ? "Player1"
                : ParticipantIdentity.CurrentId;

            EventContext.StartSession(
                Guid.NewGuid().ToString(),
                playerId,
                SceneManager.GetActiveScene().name);

            int totalTasks = TaskManager.Instance != null ? TaskManager.Instance.TotalTaskCount : 0;
            SessionEvents.RaiseSessionStarted(new SessionStartedEventArgs { TotalTasks = totalTasks });
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

        private void RefreshFocusHold()
        {
            bool lost = !HasFocus();
            if (lost == _focusLost)
            {
                return;
            }
            _focusLost = lost;

            if (lost)
            {
                SessionPause.Hold(PauseSource.ApplicationFocus);
                SafetyLog.Info("TrainingSessionManager: focus lost, session pause held.", this);
            }
            else
            {
                SessionPause.Release(PauseSource.ApplicationFocus);
                SafetyLog.Info("TrainingSessionManager: focus regained, session pause released.", this);
            }
        }

        /// <summary>
        /// OpenXR never reports session focus through OnApplicationFocus/OnApplicationPause:
        /// taking the headset off or opening the system menu leaves the XR session unfocused
        /// while the app keeps running frames, and the Activity only pauses once the headset
        /// sleeps. So the XR session state has to be polled alongside the app callbacks.
        /// </summary>
        private bool HasFocus()
        {
            if (_appPaused || !_appFocused)
            {
                return false;
            }

            if (!XRSettings.isDeviceActive)
            {
                return true;
            }

            // The session reaches FOCUSED only some frames after startup; until it has, not
            // being focused is not a focus loss.
            bool xrFocused = OpenXRUtility.IsSessionFocused;
            _xrSessionWasFocused |= xrFocused;
            return xrFocused || !_xrSessionWasFocused;
        }

        private void OnDestroy()
        {
            // Only raise here for the abort case: the session is being torn down (app quit /
            // scene unload) before it ended logically. When TaskManagerCore.EndSession already
            // published SessionEnded (normal completion or timeout), _sessionEnded is set and we
            // must not fire a second time — a double-fire toggles EventGameObjectListener twice
            // and double-broadcasts/double-logs on the dashboard/HUD.
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onSessionEnded.RemoveListener(OnSessionEnded);
                if (!_sessionEnded)
                {
                    SessionEvents.RaiseSessionEnded();
                    SafetyLog.Info("TrainingSessionManager: Session Ended event raised.", this);
                }
            }

            ScoreService.DestroyInstance();
            EventContext.Clear();
            SessionPause.Reset();
        }
    }
}
