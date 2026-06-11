using System;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scoring;
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

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            if (autoStartOnStart)
            {
                BeginSession();
            }
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

            SessionEvents.RaiseSessionStarted();
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
            if (EventBus.Instance != null)
            {
                SessionEvents.RaiseSessionEnded();
                SafetyLog.Info("TrainingSessionManager: Session Ended event raised.", this);
            }

            ScoreService.DestroyInstance();
            EventContext.Clear();
        }
    }
}
