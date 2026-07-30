using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Evaluator dashboard command handler for "recenter_player" — the R3 mid-session fix from
    /// plans/026-evaluator-recenter-playspace.md. Recenters the participant onto the current
    /// phase's anchor (see IRecenterAnchorProvider) through the shared RecenterService.
    ///
    /// No active session is deliberately NOT a rejection: recentering before SessionStarted
    /// (menu, name entry, onboarding) is the primary calibration case, not an edge case.
    ///
    /// Lives under Networking/EvaluatorDashboard/ (SafetyProto.Networking.Unity assembly) rather
    /// than Runtime/Recenter/ (SafetyProto.Runtime.Unity) so it can implement
    /// IDashboardCommandHandler: Networking.Unity already references Runtime.Unity for other
    /// reasons (e.g. TaskManager), and Runtime.Unity cannot reference Networking.Unity back
    /// without creating a cyclic assembly dependency. The namespace stays SafetyProto.Runtime for
    /// API consistency with its sibling recenter types (RecenterService, IRecenterAnchorProvider).
    /// </summary>
    public class RecenterCommandHandler : MonoBehaviour, SafetyProto.Networking.Dashboard.IDashboardCommandHandler
    {
        private const string CommandName = "recenter_player";

        [Header("Recenter")]
        [Tooltip("Shared fade -> recenter -> reground sequence and busy guard.")]
        [SerializeField] private RecenterService recenterService;
        [Tooltip("Answers where the current phase's anchor is. Must implement IRecenterAnchorProvider (e.g. PhaseController).")]
        [SerializeField] private MonoBehaviour anchorProviderBehaviour;

        [Header("Blackout message (Passo 6)")]
        [Tooltip("Popup provider (PopupService) implementing IPopupFeedback. Optional — leave empty to teleport silently.")]
        [SerializeField] private MonoBehaviour popupFeedbackProvider;
        [SerializeField] private string blackoutTitle = "Reposicionando…";
        [SerializeField] private string blackoutBody = "Você está sendo recentralizado na área de treinamento.";
        [Tooltip("Shown from the blackout onward; it outlives the fade-in so the participant can " +
                 "read it once the view returns. Auto-closes, so nothing to dismiss in VR.")]
        [SerializeField] private float blackoutMessageSeconds = 5f;

        [Header("Timing")]
        [SerializeField] private float fadeOutDuration = 0.8f;
        [SerializeField] private float holdBlackDuration = 1.0f;
        [SerializeField] private float fadeInDuration = 0.8f;

        private IRecenterAnchorProvider _anchorProvider;
        private SafetyProto.Core.Interfaces.IPopupFeedback _popupFeedback;

        public string Command => CommandName;

        private void Awake()
        {
            _anchorProvider = anchorProviderBehaviour as IRecenterAnchorProvider;
            if (anchorProviderBehaviour != null && _anchorProvider == null)
                SafetyLog.Warning("[RecenterCommandHandler] anchorProviderBehaviour não implementa IRecenterAnchorProvider.", this);

            _popupFeedback = popupFeedbackProvider as SafetyProto.Core.Interfaces.IPopupFeedback;
            if (popupFeedbackProvider != null && _popupFeedback == null)
                SafetyLog.Warning("[RecenterCommandHandler] popupFeedbackProvider não implementa IPopupFeedback.", this);
        }

        public bool TryExecute(out string reason)
        {
            if (recenterService == null)
            {
                SafetyLog.Warning("[RecenterCommandHandler] recenterService não atribuído.", this);
                reason = "Serviço de recentralização não configurado.";
                return false;
            }

            if (recenterService.IsBusy)
            {
                reason = "Transição em andamento.";
                return false;
            }

            var anchor = _anchorProvider?.CurrentAnchor;
            if (anchor == null)
            {
                SafetyLog.Warning("[RecenterCommandHandler] CurrentAnchor nulo — recentralização recusada.", this);
                reason = "Âncora da fase não configurada.";
                return false;
            }

            var options = new RecenterOptions
            {
                FadeOutDuration = fadeOutDuration,
                HoldBlackDuration = holdBlackDuration,
                FadeInDuration = fadeInDuration,
                SuspendPoseBroadcast = true,
                UseGroundProbe = false,
                LocomotorHandling = LocomotorMode.ToggleEnabled,
                OnBlackout = ShowBlackoutMessage,
                // No OnBeforeFadeIn: the popup closes itself, deliberately outliving the fade-in
                // so the message is still readable when the participant's view comes back.
            };

            StartCoroutine(recenterService.RecenterTo(anchor, options));
            reason = string.Empty;
            return true;
        }

        private void ShowBlackoutMessage()
        {
            _popupFeedback?.ShowTransient(blackoutTitle, blackoutBody, blackoutMessageSeconds);
        }
    }
}
