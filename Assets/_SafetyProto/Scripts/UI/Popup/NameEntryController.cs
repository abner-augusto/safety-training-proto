using System.Collections;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Sessions;
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
    /// Pre-session flow. In the study configuration (<c>waitForEvaluator</c>) the headset shows
    /// a "waiting for the researcher" screen and the evaluator starts the session from the
    /// dashboard with the participant code and mode, so the participant never types a code or
    /// picks a mode. A restart after an interrupted session skips the screen and starts again
    /// with the same code and mode (<see cref="SessionHandoff"/>).
    ///
    /// With <c>waitForEvaluator</c> off — development outside the study — it shows the
    /// identification popup: a numeric keypad for the participant code (7 → P-07), or the
    /// Horizon OS system keyboard for a first name, where a name or a skip gets a random
    /// anonymized id; then the mode picker. Either way it then starts the session and onboarding.
    /// </summary>
    public class NameEntryController : MonoBehaviour, IEvaluatorSessionStarter
    {
        [Header("UI")]
        [SerializeField] private PopupService popupService;
        [Tooltip("Display field for the typed value. Mirrored from the system keyboard on device, or filled by the keypad.")]
        [SerializeField] private TMP_InputField nameField;
        [SerializeField] private string confirmLabel = "Confirmar";

        [Header("Início pelo avaliador")]
        [Tooltip("Coleta: o avaliador define código e modo pelo painel, e o participante só vê a tela de espera. " +
                 "Desmarque para usar o teclado e o seletor de modo em RV (desenvolvimento).")]
        [SerializeField] private bool waitForEvaluator = true;
        [SerializeField] private string waitingTitle = "Aguardando o pesquisador";
        [SerializeField, TextArea(2, 4)]
        private string waitingBody = "O pesquisador vai iniciar o treinamento em instantes. Aguarde nesta posição.";
        [Tooltip("Sem painel conectado por este tempo na tela de espera, o óculos abre o teclado para o participante " +
                 "digitar o código (sem o botão Pular). O painel ainda pode iniciar a sessão depois. 0 = nunca.")]
        [SerializeField, Min(0f)] private float keypadFallbackSeconds = 90f;

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
        private bool _awaitingEvaluator;
        private bool _sessionStarting;
        private bool _keypadFallbackOpen;

        public bool IsAwaitingEvaluator => _awaitingEvaluator && !_sessionStarting;

        /// <summary>No dashboard showed up in time, so the participant may type the code.</summary>
        public bool IsParticipantKeypadOpen => _keypadFallbackOpen && !_sessionStarting;

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

        /// <summary>Starts the pre-session flow: a pending restart, the waiting screen, or the
        /// identification popup.</summary>
        public void Begin()
        {
            if (_sessionStarting) return;

            if (SessionHandoff.TryConsume(out var restart))
            {
                SafetyLog.Info($"[NameEntryController] Reinício: {restart.ParticipantId}, tentativa {restart.Attempt}.", this);
                StartSession(restart.ParticipantId, restart.Source, restart.Mode, restart.PreviousSessionId, restart.Attempt);
                return;
            }

            if (waitForEvaluator)
            {
                ShowWaitingScreen();
                return;
            }

            ShowIdentificationPopup(allowSkip: true);
        }

        /// <summary>The participant identifies themself: keypad code or system-keyboard name.
        /// Without the skip button, only a typed code can start the session.</summary>
        private void ShowIdentificationPopup(bool allowSkip)
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
                showSkipButton = allowSkip,
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

        private void ShowWaitingScreen()
        {
            _awaitingEvaluator = true;
            if (popupService == null)
            {
                SafetyLog.Warning("[NameEntryController] popupService não atribuído no Inspector.", this);
                return;
            }
            popupService.Show(new PopupData
            {
                type = PopupType.Normal,
                title = waitingTitle,
                body = waitingBody,
                hideIcon = true,
                hideCloseButton = true
            });
            SafetyLog.Info("[NameEntryController] Aguardando o avaliador iniciar a sessão pelo painel.", this);
            if (keypadFallbackSeconds > 0f)
                StartCoroutine(OpenKeypadWithoutEvaluator());
        }

        /// <summary>
        /// A collection must not stall when the dashboard cannot connect (laptop off the network,
        /// Wi-Fi down): after <see cref="keypadFallbackSeconds"/> with no dashboard connected the
        /// participant gets the code keypad. Any connection restarts the wait.
        /// </summary>
        private IEnumerator OpenKeypadWithoutEvaluator()
        {
            float unconnected = 0f;
            while (unconnected < keypadFallbackSeconds)
            {
                yield return null;
                if (_sessionStarting) yield break;
                unconnected = EvaluatorLink.IsConnected ? 0f : unconnected + Time.unscaledDeltaTime;
            }

            SafetyLog.Info($"[NameEntryController] Nenhum painel conectado há {keypadFallbackSeconds:0} s; teclado liberado para o participante.", this);
            _keypadFallbackOpen = true;
            ShowIdentificationPopup(allowSkip: false);
        }

        public bool TryStartFromEvaluator(string participantId, SessionMode mode, out string reason)
        {
            if (_sessionStarting)
            {
                reason = "A sessão já foi iniciada.";
                return false;
            }
            // Landing before Begin, this start replaces the restart a reset left for this scene
            // and must keep its link to the interrupted session.
            var start = new SessionStartRequest(participantId, ParticipantSources.Dashboard, mode);
            if (SessionHandoff.TryConsume(out var pending))
                start = SessionLineage.Supersede(pending, start);
            StartSession(start.ParticipantId, start.Source, start.Mode, start.PreviousSessionId, start.Attempt);
            reason = string.Empty;
            return true;
        }

        /// <summary>Starts with an identity decided outside the popup (evaluator or restart),
        /// in the same order as <see cref="Finish"/>; closes whatever popup is open.</summary>
        private void StartSession(string participantId, string source, SessionMode mode, string previousSessionId, int attempt)
        {
            _resolved = true;
            _active = false;
            _awaitingEvaluator = false;
            _keypadFallbackOpen = false;
            StopAllCoroutines();
            _keyboardOpenCoroutine = null;

            ParticipantIdentity.SetParticipantCode(participantId, source);
            SessionModeState.Current = mode;
            if (narrationVoice != null) narrationVoice.Stop();
            popupService?.Hide();

            StartSessionAndOnboarding(previousSessionId, attempt);
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
                modeSelection.Begin(() => StartSessionAndOnboarding(string.Empty, 1));
            }
            else
            {
                SessionModeState.Current = SessionMode.Guided;
                StartSessionAndOnboarding(string.Empty, 1);
            }
        }

        private void StartSessionAndOnboarding(string previousSessionId, int attempt)
        {
            // The evaluator can start the session while the mode picker is still open.
            if (_sessionStarting) return;
            _sessionStarting = true;

            if (sessionManager != null) sessionManager.BeginSession(previousSessionId, attempt);
            else SafetyLog.Warning("[NameEntryController] sessionManager não atribuído — sessão não iniciada.", this);

            if (onboarding != null) onboarding.StartSequence();

            onNameEntryFinished?.Invoke();
            SafetyLog.Info("[NameEntryController] Identificação concluída — sessão e onboarding iniciados.", this);
        }
    }
}
