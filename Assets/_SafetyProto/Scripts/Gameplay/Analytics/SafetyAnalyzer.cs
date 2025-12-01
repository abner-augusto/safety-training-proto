using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.Analytics
{
    /// <summary>
    /// Performs simple CEP on safety violations to raise critical failure signals.
    /// </summary>
    public class SafetyAnalyzer : MonoBehaviour
    {
        [SerializeField] private float windowSeconds = 30f;
        [SerializeField] private int violationThreshold = 3;

        private readonly Queue<float> _violationTimestamps = new Queue<float>();
        private bool _thresholdRaised;

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
            PruneOldViolations();

            if (_thresholdRaised && _violationTimestamps.Count < violationThreshold)
            {
                _thresholdRaised = false;
            }
        }

        private void OnSafetyViolation(SafetyViolationEventArgs args)
        {
            _violationTimestamps.Enqueue(Time.time);
            PruneOldViolations();

            if (windowSeconds <= 0f || violationThreshold <= 0)
            {
                return;
            }

            if (_violationTimestamps.Count >= violationThreshold && !_thresholdRaised)
            {
                _thresholdRaised = true;
                var payload = new CriticalSafetyFailureEventArgs
                {
                    ViolationCount = _violationTimestamps.Count,
                    WindowSeconds = windowSeconds,
                    Reason = $"{_violationTimestamps.Count} violations within {windowSeconds} seconds"
                };
                Debug.LogWarning($"SafetyAnalyzer: Critical safety failure detected ({payload.Reason}).");
                SafetyEvents.RaiseCriticalSafetyFailure(payload);
            }
        }

        private void PruneOldViolations()
        {
            if (windowSeconds <= 0f)
            {
                _violationTimestamps.Clear();
                return;
            }

            float cutoff = Time.time - windowSeconds;
            while (_violationTimestamps.Count > 0 && _violationTimestamps.Peek() < cutoff)
            {
                _violationTimestamps.Dequeue();
            }
        }
    }
}
