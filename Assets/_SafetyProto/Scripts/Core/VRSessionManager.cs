using System;
using SafetyProto.Core.Events;
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
            Debug.Log("XRSessionManager: Session Started event raised.");
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && !_isPaused)
            {
                _isPaused = true;
                SessionEvents.RaiseSessionPaused();
                Debug.Log("XRSessionManager: Session Paused event raised.");
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _isPaused)
            {
                _isPaused = false;
                SessionEvents.RaiseSessionResumed();
                Debug.Log("XRSessionManager: Session Resumed event raised.");
            }
            else if (!hasFocus && !_isPaused)
            {
                _isPaused = true;
                SessionEvents.RaiseSessionPaused();
                Debug.Log("XRSessionManager: Session Paused (due to focus loss) event raised.");
            }
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                SessionEvents.RaiseSessionEnded();
                Debug.Log("XRSessionManager: Session Ended event raised.");
            }

            EventContext.Clear();
        }
    }
}
