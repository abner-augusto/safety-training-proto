using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Core
{
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

        private void Reset()
        {
            onQuitApp ??= new UnityEvent();
            if (onQuitApp.GetPersistentEventCount() == 0)
                onQuitApp.AddListener(QuitApp);
        }
    }
}
