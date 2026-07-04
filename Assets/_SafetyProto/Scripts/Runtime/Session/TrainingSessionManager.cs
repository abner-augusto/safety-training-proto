using System;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SafetyProto.Runtime.Session
{
    public class TrainingSessionManager : MonoBehaviour
    {
        [Tooltip("Start the session automatically on scene load. Disable when a pre-session flow " +
                 "(e.g. NameEntryController) drives the start after capturing the participant id.")]
        [SerializeField] private bool autoStartOnStart = true;

        private bool _isPaused;
        private bool _sessionStarted;
        private bool _sessionEnded;

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

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && !_isPaused)
            {
                _isPaused = true;
                SessionEvents.RaiseSessionPaused();
                SafetyLog.Info("TrainingSessionManager: Session Paused event raised.", this);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _isPaused)
            {
                _isPaused = false;
                SessionEvents.RaiseSessionResumed();
                SafetyLog.Info("TrainingSessionManager: Session Resumed event raised.", this);
            }
            else if (!hasFocus && !_isPaused)
            {
                _isPaused = true;
                SessionEvents.RaiseSessionPaused();
                SafetyLog.Info("TrainingSessionManager: Session Paused (due to focus loss) event raised.", this);
            }
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
        }
    }
}
