using System.Collections;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Session;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    /// <summary>How the pre-session popup collects the participant's identification.</summary>
    public enum ParticipantInputMode
    {
        /// <summary>In-panel numeric keypad (poke + ray) for the study's participant code.</summary>
        NumericKeypad,
        /// <summary>Horizon OS system keyboard for a free-text first name.</summary>
        SystemKeyboard
    }

    /// <summary>
    /// Pre-session flow: shows an identification popup before onboarding — a numeric keypad for
    /// the study's participant code by default, or the Horizon OS system keyboard for a first
    /// name. A typed participant code becomes the participant id itself (7 → P-07); a first name
    /// or a skip gets a random anonymized id, with the name kept in the private on-device map.
    /// Then it starts the training session and kicks off onboarding.
    /// </summary>
    public class NameEntryController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private PopupService popupService;
        [Tooltip("Display field for the typed value. Mirrored from the system keyboard on device, or filled by the keypad.")]
        [SerializeField] private TMP_InputField nameField;
        [SerializeField] private string confirmLabel = "Confirmar";

        [Header("Modo de entrada")]
        [Tooltip("NumericKeypad: teclado numérico no painel para o código do participante (coleta do mestrado). " +
                 "SystemKeyboard: teclado do sistema Meta para o primeiro nome (testes fora do estudo).")]
        [SerializeField] private ParticipantInputMode inputMode = ParticipantInputMode.NumericKeypad;

        [Header("Teclado numérico (código do participante)")]
        [SerializeField] private string codeTitle = "Código do participante";
        [SerializeField, TextArea(2, 4)]
        private string codeBody = "Digite o número de participante informado pelo pesquisador.";
        [SerializeField] private string codePlaceholder = "Número";
        [Tooltip("Prefixo do id do participante, unido ao número por hífen (7 → P-07). O id resultante " +
                 "identifica a sessão nos logs, no dashboard e no menu.")]
        [SerializeField] private string codePrefix = "P";
        [SerializeField, Min(1)] private int codeMaxDigits = 3;
        [Tooltip("Zeros à esquerda até este número de dígitos (2: 7 → 07).")]
        [SerializeField, Min(1)] private int codeMinDigits = 2;
        [Tooltip("Narração tocada quando o teclado numérico abre. Opcional.")]
        [SerializeField] private AudioClip codeNarration;
        [SerializeField] private NarrationVoice narrationVoice;

        [Header("Teclado do sistema (primeiro nome)")]
        [SerializeField] private string title = "Antes de começar";
        [SerializeField, TextArea(2, 4)]
        private string body = "Digite seu primeiro nome para iniciar o treinamento. Seus dados serão anonimizados.";

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
        private string _namePlaceholder;
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
            bool useKeypad = inputMode == ParticipantInputMode.NumericKeypad;

            if (nameField != null)
            {
                nameField.text = string.Empty;
                if (nameField.placeholder is TMP_Text placeholder)
                {
                    _namePlaceholder ??= placeholder.text;
                    placeholder.text = useKeypad ? codePlaceholder : _namePlaceholder;
                }
            }

            if (nameField != null && !useKeypad)
            {
                // Meta's system keyboard attaches only to a focused, editable field.
                nameField.readOnly = false;
                SafetyLog.Info(
                    $"[NameEntryController] Campo preparado — plataforma {Application.platform}, " +
                    $"teclado suportado: {TouchScreenKeyboard.isSupported}.",
                    this);
            }

            var data = new PopupData
            {
                type = PopupType.Interactive,
                title = useKeypad ? codeTitle : title,
                body = useKeypad ? codeBody : body,
                actionButtonLabel = confirmLabel,
                onActionPressed = new UnityEvent(),
                showInputField = true,
                requireInputForAction = true,
                showNumericKeypad = useKeypad,
                hideIcon = useKeypad,
                keypadMaxDigits = codeMaxDigits,
                showSkipButton = true,
                skipButtonLabel = "Pular",
                onSkipPressed = new UnityEvent()
            };
            data.onActionPressed.AddListener(Confirm);
            data.onSkipPressed.AddListener(Skip);

            if (popupService != null)
            {
                popupService.Show(data);
                if (useKeypad && narrationVoice != null) narrationVoice.Play(codeNarration);
                if (_keyboardOpenCoroutine != null)
                    StopCoroutine(_keyboardOpenCoroutine);
                if (!useKeypad)
                    _keyboardOpenCoroutine = StartCoroutine(OpenKeyboardAfterPopup());
            }
            else
                SafetyLog.Warning("[NameEntryController] popupService não atribuído no Inspector.", this);
        }

        private IEnumerator OpenKeyboardAfterPopup()
        {
            // Wait a frame: the popup fade must make the field selectable before the OS keyboard asks for focus.
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
            if (string.IsNullOrWhiteSpace(name)) return;
            if (inputMode == ParticipantInputMode.NumericKeypad)
                Finish(() => ParticipantIdentity.SetParticipantCode(FormatCode(name.Trim())));
            else
                Finish(() => ParticipantIdentity.SetParticipant(name));
        }

        private string FormatCode(string digits) => $"{codePrefix}-{digits.PadLeft(codeMinDigits, '0')}";

        public void Skip() => Finish(() => ParticipantIdentity.SetParticipant(string.Empty));

        private void Finish(System.Action assignIdentity)
        {
            if (_resolved) return;
            _resolved = true;
            _active = false;

            if (_keyboardOpenCoroutine != null)
            {
                StopCoroutine(_keyboardOpenCoroutine);
                _keyboardOpenCoroutine = null;
            }

            assignIdentity();
            if (narrationVoice != null) narrationVoice.Stop();

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
