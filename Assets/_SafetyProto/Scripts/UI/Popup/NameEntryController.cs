using System.Collections;
using SafetyProto.Core;
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
        [Tooltip("Optional mode picker shown after the name resolves and before the session starts. When absent the session runs Guiado.")]
        [SerializeField] private SessionModeSelectionController modeSelection;
        [Tooltip("Session manager whose BeginSession() is called once the participant id is set.")]
        [SerializeField] private TrainingSessionManager sessionManager;
        [Tooltip("Frames to wait before opening, letting OVR settle (mirrors OnboardingController).")]
        [SerializeField, Min(0)] private int startDelayFrames = 10;
        [SerializeField] private bool autoStartOnEnable = true;
        [SerializeField] private int characterLimit = 40;

        /// <summary>Fired once the name flow resolves (after session + onboarding start).</summary>
        public UnityEvent onNameEntryFinished;

        private Coroutine _keyboardOpenCoroutine;
        private bool _resolved;
        private bool _active;

        private void Awake()
        {
            if (nameField != null)
            {
                nameField.characterLimit = characterLimit;
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

        /// <summary>Show the name-entry popup and focus the editable name field.</summary>
        public void Begin()
        {
            _resolved = false;
            _active = true;

            if (nameField != null)
            {
                nameField.text = string.Empty;
                // Meta's system keyboard overlay opens when an editable field receives focus.
                // Keep TMP editable so the overlay can attach to the focused field.
                nameField.readOnly = false;
                SafetyLog.Info(
                    $"[NameEntryController] Campo preparado — plataforma {Application.platform}, " +
                    $"teclado suportado: {TouchScreenKeyboard.isSupported}.",
                    this);
            }

            var data = new PopupData
            {
                type = PopupType.Interactive,
                title = title,
                body = body,
                actionButtonLabel = confirmLabel,
                onActionPressed = new UnityEvent(),
                showInputField = true,
                requireInputForAction = true,
                showSkipButton = true,
                skipButtonLabel = "Pular",
                onSkipPressed = new UnityEvent()
            };
            data.onActionPressed.AddListener(Confirm);
            data.onSkipPressed.AddListener(Skip);

            if (popupService != null)
            {
                popupService.Show(data);
                if (_keyboardOpenCoroutine != null)
                    StopCoroutine(_keyboardOpenCoroutine);
                _keyboardOpenCoroutine = StartCoroutine(OpenKeyboardAfterPopup());
            }
            else
                SafetyLog.Warning("[NameEntryController] popupService não atribuído no Inspector.", this);
        }

        private IEnumerator OpenKeyboardAfterPopup()
        {
            // PopupPanel activates the canvas and enables raycasts during its fade. Wait one
            // frame so the input field can become selectable before asking Horizon OS for input.
            yield return null;
            _keyboardOpenCoroutine = null;

            if (!_active || nameField == null) yield break;

            nameField.Select();
            nameField.ActivateInputField();
            SafetyLog.Info(
                $"[NameEntryController] Campo focado — ativo: {nameField.isActiveAndEnabled}, " +
                $"interagível: {nameField.interactable}, somente leitura: {nameField.readOnly}.",
                this);
        }

        public void Confirm()
        {
            string name = nameField != null ? nameField.text : string.Empty;
            // Require a typed name on confirm — this also gates the keyboard "Done" path.
            // An empty submission is intentional only via Skip (which assigns the anon id).
            if (string.IsNullOrWhiteSpace(name)) return;
            Finish(name);
        }

        public void Skip() => Finish(string.Empty);

        private void Finish(string name)
        {
            if (_resolved) return;
            _resolved = true;
            _active = false;

            if (_keyboardOpenCoroutine != null)
            {
                StopCoroutine(_keyboardOpenCoroutine);
                _keyboardOpenCoroutine = null;
            }

            ParticipantIdentity.SetParticipant(name);

            popupService?.Hide();

            if (modeSelection != null)
            {
                modeSelection.Begin(StartSessionAndOnboarding);
            }
            else
            {
                SessionModeState.Current = SessionMode.Guided;
                StartSessionAndOnboarding();
            }
        }

        private void StartSessionAndOnboarding()
        {
            if (sessionManager != null) sessionManager.BeginSession();
            else SafetyLog.Warning("[NameEntryController] sessionManager não atribuído — sessão não iniciada.", this);

            if (onboarding != null) onboarding.StartSequence();

            onNameEntryFinished?.Invoke();
            SafetyLog.Info("[NameEntryController] Identificação concluída — sessão e onboarding iniciados.", this);
        }
    }
}
