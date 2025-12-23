using Oculus.Interaction.OVR.Input;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using UnityEngine;

namespace SafetyProto.Utils
{
    public class ToggleActiveOnButtonPress : MonoBehaviour
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
            {
                bool newState = !target.activeSelf;
                target.SetActive(newState);
                NotifySessionState(newState);
            }
            _wasButtonActiveLastFrame = isActive;
        }

        private static void NotifySessionState(bool menuVisible)
        {
            if (EventBus.Instance == null)
                return;

            if (menuVisible)
            {
                SessionEvents.RaiseSessionPaused();
            }
            else
            {
                SessionEvents.RaiseSessionResumed();
            }
        }
    }
}
