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

        // Campo de texto opcional — usado pela tela de identificação do participante.
        public bool showInputField;

        // Quando true (e showInputField), o botão de ação fica bloqueado enquanto o
        // campo de texto estiver vazio. O participante deve digitar um nome ou usar "Pular".
        public bool requireInputForAction;

        // Auto-fecha o popup após N segundos. 0 = sem timeout (fica até ação manual).
        // Ignorado para PopupType.Interactive (exige clique do usuário).
        public float autoCloseSeconds = 0f;
    }
}
