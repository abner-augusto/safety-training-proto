using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Actions behind the operator menu's buttons: restart the training, quit the app, close the
    /// menu. The menu is a prefab, so it cannot reference the scene's <see cref="SceneLoader"/> or
    /// <see cref="PauseMenuController"/>; this finds them when a button is pressed.
    /// </summary>
    public class SessionMenu : MonoBehaviour
    {
        [Tooltip("Asks for confirmation, then quits. Auto-found on this object or its children.")]
        [SerializeField] private AppCloser appCloser;

        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip clickSound;
        [SerializeField, Range(0f, 1f)] private float clickVolume = 0.7f;

        private void Awake()
        {
            if (appCloser == null) appCloser = GetComponentInChildren<AppCloser>(true);
        }

        public void RestartTraining()
        {
            PlayClick();
            var loader = FindAnyObjectByType<SceneLoader>(FindObjectsInactive.Include);
            if (loader == null)
            {
                SafetyLog.Error("[SessionMenu] Nenhum SceneLoader na cena; não foi possível reiniciar o treinamento.", this);
                return;
            }
            loader.ResetSession();
        }

        public void QuitApp()
        {
            PlayClick();
            if (appCloser != null) appCloser.RequestQuit();
            else SafetyLog.Error("[SessionMenu] Nenhum AppCloser no menu; não foi possível fechar o aplicativo.", this);
        }

        /// <summary>Closes through the controller that opened the menu, so its pause hold is released.</summary>
        public void CloseMenu()
        {
            PlayClick();
            foreach (var controller in FindObjectsByType<PauseMenuController>(FindObjectsInactive.Include))
            {
                if (controller.target == gameObject)
                {
                    controller.CloseMenu();
                    return;
                }
            }
            gameObject.SetActive(false);
        }

        private void PlayClick()
        {
            if (clickSound == null) return;
            audioSource = AudioSourceDefaults.EnsureSpatial(this, audioSource, 0.2f, 4f);
            audioSource.PlayOneShot(clickSound, clickVolume);
        }
    }
}
