using SafetyProto.Core.Interfaces;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Core
{
    public class AppCloser : MonoBehaviour
    {
        public UnityEvent onQuitApp;

        [Header("Confirmação")]
        [Tooltip("Título do popup de confirmação exibido antes de fechar o app.")]
        [SerializeField] private string confirmTitle = "Fechar aplicativo";
        [Tooltip("Mensagem do popup de confirmação.")]
        [SerializeField] private string confirmBody = "Deseja realmente fechar o aplicativo?";
        [Tooltip("Rótulo do botão que confirma o fechamento.")]
        [SerializeField] private string confirmButtonLabel = "Fechar";
        [Tooltip("Rótulo do botão que cancela o fechamento.")]
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

        // Located by interface so this component stays decoupled from the UI assembly.
        private static IPopupFeedback FindPopupService()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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
