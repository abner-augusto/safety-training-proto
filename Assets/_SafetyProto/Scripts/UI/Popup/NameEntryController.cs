using System.Collections;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Session;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    /// <summary>
    /// Pre-session flow: shows a name-entry popup that opens the Horizon OS system keyboard before
    /// onboarding. On confirm (or skip) it assigns an anonymized participant id via
    /// <see cref="ParticipantIdentity"/>, starts the training session, then kicks off onboarding.
    /// The typed first name is stored privately by <see cref="ParticipantIdentity"/> and never
    /// leaves the device; only the id reaches logs and the dashboard.
    /// </summary>
    public class NameEntryController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private PopupService popupService;
        [Tooltip("Display field for the typed name. Mirrored from the system keyboard on device.")]
        [SerializeField] private TMP_InputField nameField;
        [SerializeField] private string title = "Antes de começar";
        [SerializeField, TextArea(2, 4)]
        private string body = "Digite seu primeiro nome para iniciar o treinamento. Seus dados serão anonimizados.";
        [SerializeField] private string confirmLabel = "Confirmar";

        [Header("Flow")]
        [Tooltip("Onboarding started after the name is submitted or skipped.")]
        [SerializeField] private OnboardingController onboarding;
        [Tooltip("Session manager whose BeginSession() is called once the participant id is set.")]
        [SerializeField] private TrainingSessionManager sessionManager;
        [Tooltip("Frames to wait before opening, letting OVR settle (mirrors OnboardingController).")]
        [SerializeField, Min(0)] private int startDelayFrames = 10;
        [SerializeField] private bool autoStartOnEnable = true;
        [SerializeField] private int characterLimit = 40;

        /// <summary>Fired once the name flow resolves (after session + onboarding start).</summary>
        public UnityEvent onNameEntryFinished;

        private TouchScreenKeyboard _keyboard;
        private bool _resolved;
        private bool _active;

        private void Awake()
        {
            if (nameField != null)
            {
                nameField.characterLimit = characterLimit;
                // Re-open the system keyboard if the field is selected again on device.
                nameField.onSelect.AddListener(_ => OpenKeyboard());
            }
        }

        private void OnEnable()
        {
            if (autoStartOnEnable) StartCoroutine(BeginDelayed());
        }

        private IEnumerator BeginDelayed()
        {
            for (int i = 0; i < startDelayFrames; i++) yield return null;
            Begin();
        }

        /// <summary>Show the name-entry popup and open the system keyboard.</summary>
        public void Begin()
        {
            _resolved = false;
            _active = true;

            if (nameField != null)
            {
                nameField.text = string.Empty;
                // On device, drive the field entirely from the system keyboard so TMP doesn't pop
                // its own; in the Editor keep it editable for physical-keyboard testing.
                nameField.readOnly = TouchScreenKeyboard.isSupported;
            }

            OpenKeyboard();

            var data = new PopupData
            {
                type = PopupType.Interactive,
                title = title,
                body = body,
                actionButtonLabel = confirmLabel,
                onActionPressed = new UnityEvent(),
                showInputField = true,
                showSkipButton = true,
                onSkipPressed = new UnityEvent()
            };
            data.onActionPressed.AddListener(Confirm);
            data.onSkipPressed.AddListener(Skip);

            if (popupService != null)
                popupService.Show(data);
            else
                SafetyLog.Warning("[NameEntryController] popupService não atribuído no Inspector.", this);
        }

        private void Update()
        {
            if (!_active || _keyboard == null) return;

            if (nameField != null && _keyboard.active)
                nameField.text = _keyboard.text;

            switch (_keyboard.status)
            {
                case TouchScreenKeyboard.Status.Done:
                    _keyboard = null;
                    Confirm();
                    break;
                case TouchScreenKeyboard.Status.Canceled:
                case TouchScreenKeyboard.Status.LostFocus:
                    _keyboard = null;
                    break;
            }
        }

        /// <summary>Opens the Horizon OS system keyboard (no-op on platforms without one).</summary>
        public void OpenKeyboard()
        {
            if (!_active || !TouchScreenKeyboard.isSupported) return; // Editor/desktop: physical typing.

            string seed = nameField != null ? nameField.text : string.Empty;
            _keyboard = TouchScreenKeyboard.Open(
                seed,
                TouchScreenKeyboardType.Default,
                autocorrection: false,
                multiline: false,
                secure: false,
                alert: false,
                textPlaceholder: "Primeiro nome");
        }

        public void Confirm()
        {
            string name = nameField != null ? nameField.text : string.Empty;
            Finish(name);
        }

        public void Skip() => Finish(string.Empty);

        private void Finish(string name)
        {
            if (_resolved) return;
            _resolved = true;
            _active = false;

            if (_keyboard != null)
            {
                _keyboard.active = false;
                _keyboard = null;
            }

            ParticipantIdentity.SetParticipant(name);

            popupService?.Hide();

            if (sessionManager != null) sessionManager.BeginSession();
            else SafetyLog.Warning("[NameEntryController] sessionManager não atribuído — sessão não iniciada.", this);

            if (onboarding != null) onboarding.StartSequence();

            onNameEntryFinished?.Invoke();
            SafetyLog.Info("[NameEntryController] Identificação concluída — sessão e onboarding iniciados.", this);
        }
    }
}
