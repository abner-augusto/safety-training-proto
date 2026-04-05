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

        [Tooltip("Ícone customizado para este step. Null = usa o ícone padrão do tipo Interactive.")]
        public Sprite icon;

        [Tooltip("GameObject a destacar quando este step abrir. Null = sem highlight.")]
        public GameObject highlightTarget;

        [Tooltip("Callback extra ao confirmar o step. Opcional.")]
        public UnityEvent onStepConfirmed;
    }
}
