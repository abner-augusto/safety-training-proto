using System.Collections;
using SafetyProto.Core.Logging;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    public class OnboardingController : MonoBehaviour
    {
        [SerializeField] private PopupService popupService;
        [SerializeField] private OnboardingStep[] steps;
        [SerializeField] private bool autoStartOnEnable = true;
        [Tooltip("Frames to wait before showing the first popup, giving OVR time to initialize stereo matrices.")]
        [SerializeField, Min(0)] private int startDelayFrames = 10;

        [Header("Skip / Completion")]
        [Tooltip("Fired when onboarding finishes — either all steps completed OR skipped. Wire this " +
                 "to whatever should start once the intro is over.")]
        public UnityEvent onOnboardingFinished;

        [Tooltip("Remember (PlayerPrefs) that onboarding was seen and auto-skip on future sessions.")]
        [SerializeField] private bool rememberSeen = false;

        private const string SeenKey = "SafetyProto.OnboardingSeen";

        private int _currentIndex = -1;
        private GameObject _activeHighlight;
        private bool _finished;

        private void OnEnable()
        {
            if (rememberSeen && PlayerPrefs.GetInt(SeenKey, 0) == 1)
            {
                SafetyLog.Info("[OnboardingController] Onboarding já visto — auto-skip.", this);
                FinishOnboarding();
                return;
            }

            if (autoStartOnEnable) StartCoroutine(StartDelayed());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            EndSequence();
        }

        private IEnumerator StartDelayed()
        {
            for (int i = 0; i < startDelayFrames; i++)
                yield return null;

            SafetyLog.Info($"[OnboardingController] StartDelayed() — delay concluído no frame {Time.frameCount}", this);
            StartSequence();
        }

        public void StartSequence()
        {
            _finished = false;
            _currentIndex = -1;
            ShowNext();
        }

        /// <summary>
        /// Skip the rest of onboarding immediately (wire a "Pular" button here). Ends the active
        /// sequence and fires <see cref="onOnboardingFinished"/>.
        /// </summary>
        public void SkipAll()
        {
            StopAllCoroutines();
            SafetyLog.Info("[OnboardingController] Onboarding pulado pelo usuário.", this);
            FinishOnboarding();
        }

        public void ShowNext()
        {
            _currentIndex++;

            if (_activeHighlight != null)
            {
                HighlightService.Disable(_activeHighlight);
                _activeHighlight = null;
            }

            if (_currentIndex >= steps.Length)
            {
                SafetyLog.Info($"[OnboardingController] ShowNext() — index {_currentIndex} out of range ({steps.Length})", this);
                FinishOnboarding();
                return;
            }

            var step = steps[_currentIndex];
            SafetyLog.Info($"[OnboardingController] ShowNext() — index {_currentIndex}, title: '{step.title}'", this);

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
                actionButtonLabel = step.actionButtonLabel,
                onActionPressed = new UnityEvent()
            };

            data.onActionPressed.AddListener(() =>
            {
                step.onStepConfirmed?.Invoke();
                ShowNext();
            });

            if (popupService == null)
            {
                SafetyLog.Warning("[OnboardingController] PopupService not assigned in Inspector.", this);
                return;
            }

            popupService.Show(data);
        }

        public void EndSequence()
        {
            if (_activeHighlight != null)
            {
                HighlightService.Disable(_activeHighlight);
                _activeHighlight = null;
            }

            popupService?.Hide();
        }

        // Single completion path (natural finish, skip, or already-seen auto-skip): tears down the
        // sequence, optionally records the seen flag, and fires onOnboardingFinished exactly once.
        private void FinishOnboarding()
        {
            if (_finished) return;
            _finished = true;

            EndSequence();

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
