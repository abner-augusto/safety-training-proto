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
        private bool _isPaused;

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            ScoreService.Instance.ResetSession();

            EventContext.StartSession(
                Guid.NewGuid().ToString(),
                "Player1",
                SceneManager.GetActiveScene().name);

            SessionEvents.RaiseSessionStarted();
            SafetyLog.Info("TrainingSessionManager: Session Started event raised.", this);
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
