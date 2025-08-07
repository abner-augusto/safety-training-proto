using UnityEngine;
public class XRSessionManager : MonoBehaviour
{
    private bool _isPaused;

    void Start()
    {
        if (!this.IsEventBusReady())
        {
            return;
        }

        EventBus.Instance.RaiseSessionStarted();
        Debug.Log("XRSessionManager: Session Started event raised.");
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && !_isPaused)
        {
            _isPaused = true;
            EventBus.Instance.RaiseSessionPaused();
            Debug.Log("XRSessionManager: Session Paused event raised.");
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && _isPaused)
        {
            _isPaused = false;
            EventBus.Instance.RaiseSessionResumed();
            Debug.Log("XRSessionManager: Session Resumed event raised.");
        }
        else if (!hasFocus && !_isPaused)
        {
            _isPaused = true;
            EventBus.Instance.RaiseSessionPaused();
            Debug.Log("XRSessionManager: Session Paused (due to focus loss) event raised.");
        }
    }

    void OnDestroy()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.RaiseSessionEnded();
            Debug.Log("XRSessionManager: Session Ended event raised.");
        }
    }
}