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

        // Opcional — sobrescreve o ícone padrão do tipo quando não-null
        public Sprite customIcon;

        // Apenas Interactive — ignorado nos outros tipos
        public string actionButtonLabel;
        public UnityEvent onActionPressed;

        // Botão secundário opcional "Pular" — usado pelo onboarding para sair da sequência.
        public bool showSkipButton;
        public UnityEvent onSkipPressed;
    }
}
