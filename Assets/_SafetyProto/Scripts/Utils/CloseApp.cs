using SafetyProto.Core.Interfaces;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Core
{
    public class AppCloser : MonoBehaviour
    {
        public UnityEvent onQuitApp;

        [Header("Confirmation")]
        [Tooltip("Title of the confirmation popup shown before closing the app.")]
        [SerializeField] private string confirmTitle = "Fechar aplicativo";
        [Tooltip("Confirmation popup message.")]
        [SerializeField] private string confirmBody = "Deseja realmente fechar o aplicativo?";
        [Tooltip("Label of the button that confirms closing.")]
        [SerializeField] private string confirmButtonLabel = "Fechar";
        [Tooltip("Label of the button that cancels closing.")]
        [SerializeField] private string cancelButtonLabel = "Cancelar";

        /// <summary>
        /// Shows a confirmation popup through the popup service and quits only if the user confirms.
        /// Wire UI buttons to this instead of <see cref="QuitApp"/> to require confirmation.
        /// Falls back to an immediate quit when no popup service is present in the scene.
        /// </summary>
        public void RequestQuit()
        {
            IPopupFeedback popup = FindPopupService();
            if (popup == null)
            {
                QuitApp();
                return;
            }

            popup.ShowConfirmation(confirmTitle, confirmBody, confirmButtonLabel, cancelButtonLabel, QuitApp);
        }

        private static IPopupFeedback FindPopupService()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb is IPopupFeedback popup)
                    return popup;
            return null;
        }

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
