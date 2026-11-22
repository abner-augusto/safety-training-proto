using System;
using System.Collections;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Runtime.PPE;
using SafetyProto.Runtime.Practice;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    /// <summary>
    /// Onboarding in two parts. The session opens in the practice area, where
    /// <see cref="practiceSteps"/> teach the interactions before the participant tries them on
    /// the props; once the area closes, <see cref="steps"/> present the training in the work site.
    /// The PPE bench stays locked until the onboarding ends, so nothing worn before the training can complete a task; anything knocked
    /// out of place meanwhile goes back to the bench at the end.
    /// </summary>
    public class OnboardingController : MonoBehaviour
    {
        [SerializeField] private PopupService popupService;
        [Tooltip("Shown in the practice area as it opens, before the participant tries the props. Each one offers 'Pular prática'.")]
        [SerializeField] private OnboardingStep[] practiceSteps;
        [Tooltip("Shown in the work site once the practice area closes: what the training is and what to do.")]
        [SerializeField] private OnboardingStep[] steps;
        [Tooltip("Plays each step's narration clip. Optional.")]
        [SerializeField] private NarrationVoice narrationVoice;
        [SerializeField] private bool autoStartOnEnable = true;
        [Tooltip("Frames to wait before showing the first popup, giving OVR time to initialize stereo matrices.")]
        [SerializeField, Min(0)] private int startDelayFrames = 10;
        [Tooltip("Where the onboarding starts. Optional: without it the practice steps are left out.")]
        [SerializeField] private PracticeArea practiceArea;
        [SerializeField] private string skipPracticeLabel = "Pular prática";

        [Header("Skip / Completion")]
        [Tooltip("Fired when onboarding finishes — either all steps completed OR skipped. Wire this " +
                 "to whatever should start once the intro is over.")]
        public UnityEvent onOnboardingFinished;

        [Tooltip("Remember (PlayerPrefs) that onboarding was seen and auto-skip on future sessions.")]
        [SerializeField] private bool rememberSeen = false;

        private const string SeenKey = "SafetyProto.OnboardingSeen";

        private readonly PpeBenchLock _benchLock = new PpeBenchLock();
        private OnboardingStep[] _sequence;
        private Action _onSequenceDone;
        private int _currentIndex = -1;
        private GameObject _activeHighlight;
        private bool _inPractice;
        private bool _finished;
        private float _startedAt;

        private void OnEnable()
        {
            if (practiceArea != null) practiceArea.ClosingStarted += OnPracticeClosing;

            if (rememberSeen && PlayerPrefs.GetInt(SeenKey, 0) == 1)
            {
                SafetyLog.Info("[OnboardingController] Onboarding já visto — auto-skip.", this);
                FinishOnboarding("skipped");
                return;
            }

            if (autoStartOnEnable) StartCoroutine(StartDelayed());
        }

        // After every Awake, so each PPE item has its grab components resolved.
        private void Start()
        {
            if (!_finished) _benchLock.Engage();
        }

        private void OnDisable()
        {
            if (practiceArea != null) practiceArea.ClosingStarted -= OnPracticeClosing;
            StopAllCoroutines();
            EndSequence();
            _benchLock.Release();
        }

        private IEnumerator StartDelayed()
        {
            for (int i = 0; i < startDelayFrames; i++)
                yield return null;

            SafetyLog.Info($"[OnboardingController] StartDelayed() — delay concluído no frame {Time.frameCount}", this);
            StartSequence();
        }

        /// <summary>Starts the onboarding in the practice area, or at the presentation when there is none.</summary>
        public void StartSequence()
        {
            _finished = false;
            _startedAt = Time.time;
            _benchLock.Engage();

            if (practiceArea != null)
            {
                _inPractice = true;
                practiceArea.Begin(onOpened: () => Play(practiceSteps, StartFreePractice), onClosed: OnPracticeClosed);
                return;
            }

            Play(steps, () => FinishOnboarding("completed"));
        }

        private void StartFreePractice()
        {
            EndSequence();
            if (practiceArea != null) practiceArea.StartFreePractice();
        }

        /// <summary>
        /// Skip the rest of onboarding immediately (wire a "Pular" button here). Ends the active
        /// sequence and fires <see cref="onOnboardingFinished"/>.
        /// </summary>
        public void SkipAll()
        {
            StopAllCoroutines();
            SafetyLog.Info("[OnboardingController] Onboarding pulado pelo usuário.", this);
            if (_inPractice && practiceArea != null) practiceArea.Close("skipped");
            FinishOnboarding("skipped");
        }

        /// <summary>Leaves the practice area for the presentation (the practice popups' button).</summary>
        public void SkipPractice()
        {
            if (!_inPractice || practiceArea == null || !practiceArea.IsOpen) return;
            SafetyLog.Info("[OnboardingController] Prática pulada pelo participante.", this);
            practiceArea.Close("skipped");
        }

        // Whoever closes the area (Continuar, the skip button, the countdown, the evaluator), the
        // practice popup must not linger into the fade.
        private void OnPracticeClosing()
        {
            if (!_inPractice) return;
            _sequence = null;
            _onSequenceDone = null;
            EndSequence();
        }

        private void OnPracticeClosed()
        {
            _inPractice = false;
            if (_finished) return;
            Play(steps, () => FinishOnboarding("completed"));
        }

        private void Play(OnboardingStep[] sequence, Action onDone)
        {
            _sequence = sequence ?? Array.Empty<OnboardingStep>();
            _onSequenceDone = onDone;
            _currentIndex = -1;
            ShowNext();
        }

        public void ShowNext()
        {
            _currentIndex++;
            ClearHighlight();

            if (_sequence == null) return;
            if (_currentIndex >= _sequence.Length)
            {
                SafetyLog.Info($"[OnboardingController] ShowNext() — index {_currentIndex} out of range ({_sequence.Length})", this);
                var done = _onSequenceDone;
                _sequence = null;
                _onSequenceDone = null;
                done?.Invoke();
                return;
            }

            var step = _sequence[_currentIndex];
            SafetyLog.Info($"[OnboardingController] ShowNext() — index {_currentIndex}, title: '{step.title}'", this);
            InteractionEvents.RaiseOnboardingStep(_inPractice ? "practice" : "presentation", _currentIndex + 1, step.title);

            if (step.highlightTarget != null)
            {
                HighlightService.Enable(step.highlightTarget);
                _activeHighlight = step.highlightTarget;
            }

            var data = new PopupData
            {
                type = PopupType.Interactive,
                title = step.title,
                body = step.body,
                customIcon = step.icon,
                illustrationFrames = step.illustrationFrames,
                actionButtonLabel = step.actionButtonLabel,
                onActionPressed = new UnityEvent()
            };

            data.onActionPressed.AddListener(() =>
            {
                step.onStepConfirmed?.Invoke();
                ShowNext();
            });

            if (_inPractice)
            {
                data.showSkipButton = true;
                data.skipButtonLabel = skipPracticeLabel;
                data.onSkipPressed = new UnityEvent();
                data.onSkipPressed.AddListener(SkipPractice);
            }
            else if (_currentIndex == 0)
            {
                data.showSkipButton = true;
                data.skipButtonLabel = "Pular";
                data.onSkipPressed = new UnityEvent();
                data.onSkipPressed.AddListener(SkipAll);
            }

            if (popupService == null)
            {
                SafetyLog.Warning("[OnboardingController] PopupService not assigned in Inspector.", this);
                return;
            }

            popupService.Show(data);
            if (narrationVoice != null) narrationVoice.Play(step.narration);
        }

        public void EndSequence()
        {
            if (narrationVoice != null) narrationVoice.Stop();
            ClearHighlight();
            popupService?.Hide();
        }

        private void ClearHighlight()
        {
            if (_activeHighlight == null) return;
            HighlightService.Disable(_activeHighlight);
            _activeHighlight = null;
        }

        /// <param name="reason"><c>completed</c> or <c>skipped</c>, for the log.</param>
        private void FinishOnboarding(string reason)
        {
            if (_finished) return;
            _finished = true;
            InteractionEvents.RaiseOnboardingFinished(reason, Time.time - _startedAt);

            _sequence = null;
            _onSequenceDone = null;
            EndSequence();
            _benchLock.Release();
            SafetyLog.Info($"[OnboardingController] Bancada liberada. {LooseObjectRecall.RecallAll().Describe()}", this);

            if (rememberSeen)
            {
                PlayerPrefs.SetInt(SeenKey, 1);
                PlayerPrefs.Save();
            }

            onOnboardingFinished?.Invoke();
            SafetyLog.Info("[OnboardingController] Onboarding finalizado.", this);
        }
    }
}
