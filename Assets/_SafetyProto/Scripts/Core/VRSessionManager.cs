using System;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SafetyProto.Core
{
    public class XRSessionManager : MonoBehaviour
    {
        private bool _isPaused;

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            EventContext.StartSession(
                Guid.NewGuid().ToString(),
                "Player1",
                SceneManager.GetActiveScene().name);

            SessionEvents.RaiseSessionStarted();
            SafetyLog.Info("XRSessionManager: Session Started event raised.", this);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && !_isPaused)
            {
                _isPaused = true;
                SessionEvents.RaiseSessionPaused();
                SafetyLog.Info("XRSessionManager: Session Paused event raised.", this);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _isPaused)
            {
                _isPaused = false;
                SessionEvents.RaiseSessionResumed();
                SafetyLog.Info("XRSessionManager: Session Resumed event raised.", this);
            }
            else if (!hasFocus && !_isPaused)
            {
                _isPaused = true;
                SessionEvents.RaiseSessionPaused();
                SafetyLog.Info("XRSessionManager: Session Paused (due to focus loss) event raised.", this);
            }
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                SessionEvents.RaiseSessionEnded();
                SafetyLog.Info("XRSessionManager: Session Ended event raised.", this);
            }

            EventContext.Clear();
        }
    }
}
