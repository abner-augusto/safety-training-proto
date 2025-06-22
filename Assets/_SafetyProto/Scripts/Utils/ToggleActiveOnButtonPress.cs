using UnityEngine;
using Oculus.Interaction.OVR.Input;

public class ToggleActiveOnButtonPress : MonoBehaviour
{
    [Header("References")]
    public OVRButtonActiveState buttonState; // Assign your OVRButtonActiveState component
    public GameObject target; // The GameObject you want to toggle

    private bool _wasButtonActiveLastFrame;

    void Update()
    {
        if (buttonState == null || target == null)
            return;

        // Detect a button down event (transition from false to true)
        bool isActive = buttonState.Active;
        if (isActive && !_wasButtonActiveLastFrame)
        {
            // Toggle the target's active state
            target.SetActive(!target.activeSelf);
        }
        _wasButtonActiveLastFrame = isActive;
    }
}