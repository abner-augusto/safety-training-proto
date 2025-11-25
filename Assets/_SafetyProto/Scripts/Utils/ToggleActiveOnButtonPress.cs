using Oculus.Interaction.OVR.Input;
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
                target.SetActive(!target.activeSelf);
            }
            _wasButtonActiveLastFrame = isActive;
        }
    }
}
