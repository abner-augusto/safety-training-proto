using Oculus.Interaction.OVR.Input;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using UnityEngine;

namespace SafetyProto.Utils
{
    /// <summary>
    /// Shows/hides a menu GameObject via a controller button and keeps the session pause state in
    /// sync: opening the menu raises SessionPaused, closing it raises SessionResumed, so the timer
    /// and other pause-aware systems stay balanced. Exposes <see cref="CloseMenu"/> so an in-menu
    /// "close" button can dismiss the menu (and resume) without the controller button.
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        [Header("References")]
        public OVRButtonActiveState buttonState;
        public GameObject target;

        private bool _wasButtonActiveLastFrame;

        private void Update()
        {
            if (buttonState == null || target == null)
                return;

            bool isActive = buttonState.Active;
            if (isActive && !_wasButtonActiveLastFrame)
                SetMenuVisible(!target.activeSelf);

            _wasButtonActiveLastFrame = isActive;
        }

        /// <summary>
        /// Closes the menu and resumes the session. Wire in-menu "close" buttons to this so the
        /// SessionPaused raised when the menu opened is balanced by a SessionResumed.
        /// </summary>
        public void CloseMenu()
        {
            if (target == null || !target.activeSelf)
                return;

            SetMenuVisible(false);
            // Keep the controller-button edge detector in sync so the next press re-opens cleanly.
            _wasButtonActiveLastFrame = buttonState != null && buttonState.Active;
        }

        private void SetMenuVisible(bool visible)
        {
            target.SetActive(visible);
            NotifySessionState(visible);
        }

        private static void NotifySessionState(bool menuVisible)
        {
            if (EventBus.Instance == null)
                return;

            if (menuVisible)
                SessionEvents.RaiseSessionPaused();
            else
                SessionEvents.RaiseSessionResumed();
        }
    }
}
