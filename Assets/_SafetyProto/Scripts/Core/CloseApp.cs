using UnityEngine;
using UnityEngine.Events;

public class AppCloser : MonoBehaviour
{
    public UnityEvent onQuitApp;

    public void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // For convenience, link in inspector
    private void Reset()
    {
        if (onQuitApp == null)
            onQuitApp = new UnityEvent();
        onQuitApp.AddListener(QuitApp);
    }
}