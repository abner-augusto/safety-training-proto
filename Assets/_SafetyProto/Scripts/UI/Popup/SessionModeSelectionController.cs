using System;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Pre-session mode picker shown after name entry and before the session starts.
    /// Confirm selects Guiado, cancel selects Avaliação.
    /// </summary>
    public class SessionModeSelectionController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private PopupService popupService;
        [SerializeField] private string title = "Modo da sessão";
        [SerializeField, TextArea(2, 4)]
        private string body = "Guiado: o painel lista cada tarefa e a atividade só finaliza completa.\n" +
                              "Avaliação: apenas o objetivo geral é exibido e é possível finalizar com omissões.";
        [SerializeField] private string guidedLabel = "Guiado";
        [SerializeField] private string evaluationLabel = "Avaliação";

        public void Begin(Action onChosen)
        {
            if (popupService == null)
            {
                SafetyLog.Warning("[SessionModeSelection] popupService não atribuído — modo Guiado assumido.", this);
                SessionModeState.Current = SessionMode.Guided;
                onChosen?.Invoke();
                return;
            }

            popupService.ShowConfirmation(
                title, body,
                confirmLabel: guidedLabel,
                cancelLabel: evaluationLabel,
                onConfirm: () => Choose(SessionMode.Guided, onChosen),
                onCancel: () => Choose(SessionMode.Evaluation, onChosen));
        }

        private void Choose(SessionMode mode, Action onChosen)
        {
            SessionModeState.Current = mode;
            SafetyLog.Info($"[SessionModeSelection] Modo da sessão: {SessionModeState.CurrentName}.", this);
            onChosen?.Invoke();
        }
    }
}
