using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    [System.Serializable]
    public class PopupData
    {
        public PopupType type;
        public string title;
        [TextArea(2, 5)]
        public string body;

        // Optional — overrides the default icon for this type when non-null
        public Sprite customIcon;

        // Interactive only — ignored for other types
        public string actionButtonLabel;
        public UnityEvent onActionPressed;

        // Optional secondary "Skip" button — used by onboarding to exit the sequence
        // and as the "Cancel" button in confirmations.
        public bool showSkipButton;
        public UnityEvent onSkipPressed;

        // Optional label for the secondary button (e.g. "Cancel"). Empty keeps the authored
        // text in the prefab ("Skip").
        public string skipButtonLabel;

        // Optional text field — used by the participant identification screen.
        public bool showInputField;

        // When true (and showInputField), the action button is blocked while the text field
        // is empty. The participant must type a name or use "Skip".
        public bool requireInputForAction;

        // Auto-closes the popup after N seconds. 0 = no timeout (stays until manual action).
        // Ignored for PopupType.Interactive (requires a user click).
        public float autoCloseSeconds = 0f;
    }
}
