using UnityEngine;
public class XRSessionManager : MonoBehaviour
{
    private bool _isPaused;

    void Start()
    {
        // Access the singleton instance directly
        if (EventBus.Instance == null)
        {
            Debug.LogError("XRSessionManager requires an EventBus, but the instance is not available.", this);
            return;
        }

        EventBus.Instance.RaiseSessionStarted();
        Debug.Log("XRSessionManager: Session Started event raised.");
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (EventBus.Instance == null) return;
        if (pauseStatus && !_isPaused)
        {
            _isPaused = true;
            EventBus.Instance.RaiseSessionPaused();
            Debug.Log("XRSessionManager: Session Paused event raised.");
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (EventBus.Instance == null) return;
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