using UnityEngine;

public class XRSessionManager : MonoBehaviour
{
    [Tooltip("Assign your EventBus ScriptableObject asset here.")]
    public EventBus eventBus;

    private bool _isPaused = false;

    void Start()
    {
        if (eventBus == null)
        {
            Debug.LogError("EventBus not assigned to XRSessionManager!", this);
            return;
        }
        // Make this EventBus the globally accessible one if using the static Instance property strategy
        // EventBus.Instance = eventBus; // This makes the assigned bus the one for static access

        eventBus.RaiseSessionStarted();
        Debug.Log("XRSessionManager: Session Started event raised.");
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (eventBus == null) return;

        if (pauseStatus && !_isPaused)
        {
            _isPaused = true;
            eventBus.RaiseSessionPaused();
            Debug.Log("XRSessionManager: Session Paused event raised.");
        }
        // Note: OnApplicationFocus is often better for VR resume
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (eventBus == null) return;

        // This handles VR headset putting on/taking off (HMD mounted/unmounted)
        if (hasFocus && _isPaused)
        {
            _isPaused = false;
            eventBus.RaiseSessionResumed();
            Debug.Log("XRSessionManager: Session Resumed event raised.");
        }
        else if (!hasFocus && !_isPaused) // Check if not already paused by OnApplicationPause
        {
            _isPaused = true; // Set paused state
            eventBus.RaiseSessionPaused(); // Raise paused if focus is lost and not already paused
            Debug.Log("XRSessionManager: Session Paused (due to focus loss) event raised.");
        }
    }

    void OnDestroy()
    {
        if (eventBus == null) return;
        eventBus.RaiseSessionEnded();
        Debug.Log("XRSessionManager: Session Ended event raised.");
    }
}