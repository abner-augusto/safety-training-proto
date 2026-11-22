using System.Collections;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Sessions;
using SafetyProto.Runtime.Session;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace SafetyProto.UI
{
    /// <summary>
    /// Pre-session flow. In the study configuration (<c>waitForEvaluator</c>) the headset shows
    /// a "waiting for the researcher" screen and the evaluator starts the session from the
    /// dashboard with the participant code and mode, so the participant never types a code or
    /// picks a mode. A restart after an interrupted session skips the screen and starts again
    /// with the same code and mode (<see cref="SessionHandoff"/>).
    ///
    /// With <c>waitForEvaluator</c> off — development outside the study — it shows the
    /// identification popup: a numeric keypad for the participant code (7 → P-07, normalized by
    /// <see cref="ParticipantCode"/> like the dashboard's), where "Pular" gets a random id; then
    /// the mode picker. Either way it then starts the session and onboarding.
    /// </summary>
    public class NameEntryController : MonoBehaviour, IEvaluatorSessionStarter
    {
        [Header("UI")]
        [SerializeField] private PopupService popupService;
        [Tooltip("Shows the digits typed on the keypad.")]
        [SerializeField, FormerlySerializedAs("nameField")] private TMP_InputField codeField;
        [SerializeField] private string confirmLabel = "Confirmar";

        [Header("Evaluator Start")]
        [Tooltip("Data collection: the evaluator sets code and mode from the dashboard, and the " +
                 "participant only sees the waiting screen. Uncheck to use the keypad and the mode " +
                 "picker in VR (development).")]
        [SerializeField] private bool waitForEvaluator = true;
        [SerializeField] private string waitingTitle = "Aguardando o pesquisador";
        [SerializeField, TextArea(2, 4)]
        private string waitingBody = "O pesquisador vai iniciar o treinamento em instantes. Aguarde nesta posição.";
        [Tooltip("With no dashboard connected for this long on the waiting screen, the headset " +
                 "opens the keypad for the participant to type the code (without the 'Pular' " +
                 "button). The dashboard can still start the session later. 0 = never.")]
        [SerializeField, Min(0f)] private float keypadFallbackSeconds = 90f;

        [Header("Numeric Keypad (participant code)")]
        [SerializeField] private string codeTitle = "Código do participante";
        [SerializeField, TextArea(2, 4)]
        private string codeBody = "Digite o número de participante informado pelo pesquisador.";
        [SerializeField] private string codePlaceholder = "Número";
        [Tooltip("Narration played when the numeric keypad opens. Optional.")]
        [SerializeField] private AudioClip codeNarration;
        [SerializeField] private NarrationVoice narrationVoice;

        [Header("Flow")]
        [Tooltip("Onboarding started after the name is submitted or skipped.")]
        [SerializeField] private OnboardingController onboarding;
        [Tooltip("Optional mode picker shown after the name resolves and before the session starts. When absent the session runs in Guided mode.")]
        [SerializeField] private SessionModeSelectionController modeSelection;
        [Tooltip("Session manager whose BeginSession() is called once the participant id is set.")]
        [SerializeField] private TrainingSessionManager sessionManager;
        [Tooltip("Frames to wait before opening, letting OVR settle (mirrors OnboardingController).")]
        [SerializeField, Min(0)] private int startDelayFrames = 10;
        [SerializeField] private bool autoStartOnEnable = true;

        /// <summary>Fired once the name flow resolves (after session + onboarding start).</summary>
        public UnityEvent onNameEntryFinished;

        private bool _resolved;
        private bool _awaitingEvaluator;
        private bool _sessionStarting;
        private bool _keypadFallbackOpen;

        public bool IsAwaitingEvaluator => _awaitingEvaluator && !_sessionStarting;

        /// <summary>No dashboard showed up in time, so the participant may type the code.</summary>
        public bool IsParticipantKeypadOpen => _keypadFallbackOpen && !_sessionStarting;

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
                StartSession(restart);
                return;
            }

            if (waitForEvaluator)
            {
                ShowWaitingScreen();
                return;
            }

            ShowIdentificationPopup(allowSkip: true);
        }

        /// <summary>The participant types their code on the keypad. Without the skip button, only
        /// a typed code can start the session.</summary>
        private void ShowIdentificationPopup(bool allowSkip)
        {
            _resolved = false;

            if (codeField != null)
            {
                codeField.text = string.Empty;
                if (codeField.placeholder is TMP_Text placeholder) placeholder.text = codePlaceholder;
            }

            var data = new PopupData
            {
                type = PopupType.Interactive,
                title = codeTitle,
                body = codeBody,
                actionButtonLabel = confirmLabel,
                onActionPressed = new UnityEvent(),
                showInputField = true,
                requireInputForAction = true,
                showNumericKeypad = true,
                hideIcon = true,
                keypadMaxDigits = ParticipantCode.MaxDigits,
                showSkipButton = allowSkip,
                skipButtonLabel = "Pular",
                onSkipPressed = new UnityEvent()
            };
            data.onActionPressed.AddListener(Confirm);
            data.onSkipPressed.AddListener(Skip);

            if (popupService != null)
            {
                popupService.Show(data);
                if (narrationVoice != null) narrationVoice.Play(codeNarration);
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

        public bool TryStartFromEvaluator(string participantId, SessionMode mode, bool skipPractice, out string reason)
        {
            if (_sessionStarting)
            {
                reason = "A sessão já foi iniciada.";
                return false;
            }
            // Landing before Begin, this start replaces the restart a reset left for this scene
            // and must keep its link to the interrupted session.
            var start = new SessionStartRequest(participantId, ParticipantSources.Dashboard, mode,
                                                skipPractice: skipPractice);
            if (SessionHandoff.TryConsume(out var pending))
                start = SessionLineage.Supersede(pending, start);
            StartSession(start);
            reason = string.Empty;
            return true;
        }

        /// <summary>Starts with an identity decided outside the popup (evaluator or restart),
        /// in the same order as <see cref="Finish"/>; closes whatever popup is open.</summary>
        private void StartSession(SessionStartRequest start)
        {
            _resolved = true;
            _awaitingEvaluator = false;
            _keypadFallbackOpen = false;
            StopAllCoroutines();

            ParticipantIdentity.SetParticipantCode(start.ParticipantId, start.Source);
            SessionModeState.Current = start.Mode;
            if (narrationVoice != null) narrationVoice.Stop();
            popupService?.Hide();

            StartSessionAndOnboarding(start.PreviousSessionId, start.Attempt, start.SkipPractice);
        }

        public void Confirm()
        {
            string digits = codeField != null ? codeField.text : string.Empty;
            if (!ParticipantCode.TryNormalize(digits, out var code, out var error))
            {
                SafetyLog.Info($"[NameEntryController] Código recusado: {error}", this);
                return;
            }
            Finish(() => ParticipantIdentity.SetParticipantCode(code));
        }

        public void Skip() => Finish(() => ParticipantIdentity.SetAnonymousParticipant());

        private void Finish(System.Action assignIdentity)
        {
            if (_resolved) return;
            _resolved = true;

            assignIdentity();
            if (narrationVoice != null) narrationVoice.Stop();

            popupService?.Hide();

            if (modeSelection != null)
            {
                modeSelection.Begin(() => StartSessionAndOnboarding(string.Empty, 1, skipPractice: false));
            }
            else
            {
                SessionModeState.Current = SessionMode.Guided;
                StartSessionAndOnboarding(string.Empty, 1, skipPractice: false);
            }
        }

        private void StartSessionAndOnboarding(string previousSessionId, int attempt, bool skipPractice)
        {
            // The evaluator can start the session while the mode picker is still open.
            if (_sessionStarting) return;
            _sessionStarting = true;

            if (sessionManager != null) sessionManager.BeginSession(previousSessionId, attempt, skipPractice);
            else SafetyLog.Warning("[NameEntryController] sessionManager não atribuído — sessão não iniciada.", this);

            if (onboarding != null) onboarding.StartSequence(skipPractice);

            onNameEntryFinished?.Invoke();
            SafetyLog.Info("[NameEntryController] Identificação concluída — sessão e onboarding iniciados.", this);
        }
    }
}
