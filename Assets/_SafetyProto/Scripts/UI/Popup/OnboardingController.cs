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
        [Tooltip("Frames to wait before showing the first popup, giving OVR time to initialize stereo matrices.\n" +
                 "Quest 3 precisa de ~10 frames para inicializar stereo matrices e OVROverlayCanvas.")]
        // Fix C: raised from 3 to 10 — Quest 3 needs more frames to initialise stereo matrices
        // before showing a popup backed by OVROverlayCanvas without stalling the compositor.
        [SerializeField, Min(0)] private int startDelayFrames = 10;

        private int _currentIndex = -1;
        private GameObject _activeHighlight;

        private void OnEnable()  { if (autoStartOnEnable) StartCoroutine(StartDelayed()); }
        private void OnDisable() { StopAllCoroutines(); EndSequence(); }

        private IEnumerator StartDelayed()
        {
            for (int i = 0; i < startDelayFrames; i++)
                yield return null;

            SafetyLog.Info($"[OnboardingController] StartDelayed() — delay concluído no frame {Time.frameCount}, iniciando sequência.", this);
            StartSequence();
        }

        public void StartSequence()
        {
            _currentIndex = -1;
            ShowNext();
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
                SafetyLog.Info($"[OnboardingController] ShowNext() — índice {_currentIndex} fora dos steps ({steps.Length}), encerrando sequência.", this);
                EndSequence();
                return;
            }

            var step = steps[_currentIndex];
            SafetyLog.Info($"[OnboardingController] ShowNext() — índice {_currentIndex}, título: '{step.title}'", this);

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
                SafetyLog.Warning("[OnboardingController] PopupService não atribuído no Inspector.", this);
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

            if (popupService != null) popupService.Hide();
        }
    }
}
