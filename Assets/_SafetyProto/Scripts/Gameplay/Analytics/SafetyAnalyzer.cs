using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.Analytics
{
    /// SafetyAnalyzer
    /// Listens to safety violation events and uses SafetyPatternDetector (CEP core)
    /// to detect repeated violations in a sliding time window. When the threshold
    /// is reached, raises a CriticalSafetyFailure event for analytics and feedback.
    public class SafetyAnalyzer : MonoBehaviour
    {
        [SerializeField, Tooltip("Time window in seconds for repeated violations.")]
        private float windowSeconds = 30f;
        [SerializeField, Tooltip("Number of violations within the window to emit a critical failure.")]
        private int violationThreshold = 3;

        private SafetyPatternDetector _detector;
        private bool _thresholdRaised;

        private void Awake()
        {
            _detector = new SafetyPatternDetector(windowSeconds, violationThreshold);
        }

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            EventBus.Instance.onSafetyViolation.AddListener(OnSafetyViolation);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
            }
        }

        private void Update()
        {
            if (_detector == null)
            {
                return;
            }

            _detector.Prune(Time.time);

            if (_thresholdRaised && _detector.CurrentCount < violationThreshold)
            {
                _thresholdRaised = false;
            }
        }

        private void OnSafetyViolation(SafetyViolationEventArgs args)
        {
            if (_detector == null || windowSeconds <= 0f || violationThreshold <= 0)
            {
                return;
            }

            var now = Time.time;
            bool thresholdReached = _detector.RecordViolation(now);

            if (thresholdReached && !_thresholdRaised)
            {
                _thresholdRaised = true;
                var payload = new CriticalSafetyFailureEventArgs
                {
                    ViolationCount = _detector.CurrentCount,
                    WindowSeconds = windowSeconds,
                    Reason = $"{_detector.CurrentCount} violations within {windowSeconds} seconds"
                };
                Debug.LogWarning($"SafetyAnalyzer: Critical safety failure detected ({payload.Reason}).");
                SafetyEvents.RaiseCriticalSafetyFailure(payload);
            }
            else if (!thresholdReached && _thresholdRaised && _detector.CurrentCount < violationThreshold)
            {
                _thresholdRaised = false;
            }
        }
    }
}
