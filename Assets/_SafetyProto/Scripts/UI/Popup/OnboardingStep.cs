using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    [System.Serializable]
    public class OnboardingStep
    {
        public string title;
        [TextArea(2, 5)] public string body;
        public string actionButtonLabel = "Prosseguir";

        [Tooltip("Custom icon for this step. Null = the default icon of the Interactive type.")]
        public Sprite icon;

        [Tooltip("Illustration in place of the icon. With more than one frame, they alternate with a crossfade (e.g. open hand → closed fist).")]
        public Sprite[] illustrationFrames;

        [Tooltip("Narration played when this step opens. Null = no narration.")]
        public AudioClip narration;

        [Tooltip("GameObject to highlight when this step opens. Null = no highlight.")]
        public GameObject highlightTarget;

        [Tooltip("Extra callback when the step is confirmed. Optional.")]
        public UnityEvent onStepConfirmed;

        [Tooltip("Confirming this step opens the practice area; the next step shows when it closes.")]
        public bool opensPracticeArea;
    }
}
