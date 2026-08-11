using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.Runtime.Safety
{
    /// <summary>
    /// Turns a completed gaze dwell on a hazard into a reportable action.
    ///
    /// Flow: dwell completes -> the report button materialises next to the defect and stays there
    /// for the rest of the session -> pressing it asks for confirmation -> confirming publishes the
    /// authored ActionId.
    ///
    /// The button is revealed regardless of whether the participant is wearing the PPE the task
    /// requires. TaskManager already validates requiredPPE on the attempt, and the scenario already
    /// authors a ppeAdvice explaining why the inspection has to happen with the lanyard connected —
    /// letting the attempt fail with that advice is the teaching moment. Hiding the button instead
    /// would just read as "there is nothing here".
    /// </summary>
    public class SafetyIssueReporter : MonoBehaviour, ISessionResettable
    {
        [Header("Action")]
        [Tooltip("ActionId published on confirmation. Must exist in Resources/Actions/actions.json.")]
        [SerializeField] private string _actionId = "flag_safety_net";

        [Tooltip("Free-form context recorded with the action attempt, for session log and dashboard.")]
        [SerializeField] private string _actionContext = "gaze_dwell";

        [Header("References")]
        [Tooltip("Dwell that reveals the button when it completes.")]
        [SerializeField] private GazeDwellTarget _dwellTarget;

        [Tooltip("Button the participant presses to open the confirmation. Subscribed in code — do " +
                 "not also wire its OnClick to Report(), or the popup opens twice.")]
        [SerializeField] private DualModeButton _reportButton;

        [Tooltip("Root object switched on when the dwell completes and off once the report is filed. " +
                 "Usually the button's parent, so its visuals and colliders go together.")]
        [SerializeField] private GameObject _reportButtonRoot;

        [Tooltip("Component implementing IPopupFeedback (the popup controller).")]
        [SerializeField] private MonoBehaviour _popupFeedbackProvider;

        [Header("Dwell completion feedback")]
        [Tooltip("Optional. Plays once when the dwell completes and the button appears.")]
        [SerializeField] private AudioSource _audioSource;

        [SerializeField] private AudioClip _dwellCompletedSound;

        [Tooltip("Optional. Pulses the controllers when the dwell completes.")]
        [SerializeField] private HapticManager _hapticManager;

        [SerializeField, Range(0f, 1f)] private float _dwellHapticAmplitude = 0.25f;
        [SerializeField, Range(0f, 0.5f)] private float _dwellHapticDuration = 0.06f;

        [Header("Popup copy — participant facing, Portuguese")]
        [SerializeField] private string _popupTitle = "Reportar Irregularidade";

        [TextArea(2, 4)]
        [SerializeField] private string _popupBody =
            "Você identificou uma irregularidade na tela fachadeira. Deseja reportá-la?";

        [SerializeField] private string _confirmLabel = "Reportar";
        [SerializeField] private string _cancelLabel = "Cancelar";

        /// <summary>
        /// Injectable popup dependency. Falls back to the serialized provider when unset, which is
        /// how the scene wires it; tests set it directly.
        /// </summary>
        public IPopupFeedback PopupFeedback { get; set; }

        /// <summary>True once the participant has confirmed the report.</summary>
        public bool HasReported { get; private set; }

        /// <summary>How many times the confirmation was opened and backed out of.</summary>
        public int CancelledReportCount { get; private set; }

        private bool _confirmationOpen;

        private void Awake() => HideButton();

        private void OnEnable()
        {
            if (_dwellTarget != null) _dwellTarget.Completed += HandleDwellCompleted;
            if (_reportButton != null) _reportButton.Clicked += Report;
        }

        private void OnDisable()
        {
            if (_dwellTarget != null) _dwellTarget.Completed -= HandleDwellCompleted;
            if (_reportButton != null) _reportButton.Clicked -= Report;
        }

        private void HandleDwellCompleted()
        {
            if (HasReported) return;

            ShowButton();

            // The button can appear at the edge of vision, and a blink can swallow the last frames
            // of the fill. Announce the transition on two channels the participant cannot miss.
            if (_audioSource != null && _dwellCompletedSound != null)
                _audioSource.PlayOneShot(_dwellCompletedSound);

            if (_hapticManager != null)
                _hapticManager.Pulse(_dwellHapticAmplitude, _dwellHapticDuration);

            SafetyLog.Info($"[SafetyIssueReporter] Dwell completo em '{name}'; botão de reporte disponível.", this);
        }

        /// <summary>Opens the confirmation. Raised by the report button.</summary>
        public void Report()
        {
            if (HasReported || _confirmationOpen) return;

            var popup = PopupFeedback ?? _popupFeedbackProvider as IPopupFeedback;
            if (popup == null)
            {
                SafetyLog.Warning("[SafetyIssueReporter] IPopupFeedback indisponível; publicando o reporte sem confirmação.", this);
                PublishReport();
                return;
            }

            _confirmationOpen = true;
            popup.ShowConfirmation(_popupTitle, _popupBody, _confirmLabel, _cancelLabel,
                onConfirm: () => { _confirmationOpen = false; PublishReport(); },
                onCancel: () =>
                {
                    _confirmationOpen = false;
                    CancelledReportCount++;
                    SafetyLog.Info($"[SafetyIssueReporter] Reporte cancelado ({CancelledReportCount}x).", this);
                });
        }

        private void PublishReport()
        {
            if (HasReported) return;
            HasReported = true;

            ActionEvents.PublishActionAttempt(
                _actionId,
                sourceId: name,
                context: _actionContext,
                position: transform.position);

            HideButton();
        }

        public void ResetSession()
        {
            HasReported = false;
            CancelledReportCount = 0;
            _confirmationOpen = false;
            if (_dwellTarget != null) _dwellTarget.ResetDwell();
            HideButton();
        }

        private void ShowButton()
        {
            if (_reportButtonRoot != null) _reportButtonRoot.SetActive(true);
        }

        private void HideButton()
        {
            if (_reportButtonRoot != null) _reportButtonRoot.SetActive(false);
        }
    }
}
