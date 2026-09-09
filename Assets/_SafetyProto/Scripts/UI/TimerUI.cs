using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    public class TimerUI : MonoBehaviour
    {
        [Tooltip("Assign your TimerSystem here (not EventBus).")]
        public TimerSystem timerSystem;

        [Tooltip("Rótulo acima do valor. Alterna entre contagem regressiva e cronômetro.")]
        [SerializeField] private TextMeshProUGUI timerLabel;
        [SerializeField] private string countdownLabel = "TEMPO RESTANTE";
        [SerializeField] private string stopwatchLabel = "TEMPO";

        private TextMeshProUGUI _timerText;
        private int _lastDisplayedSecond = -1;
        private bool _labelApplied;
        private bool _labelIsStopwatch;

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            _timerText = GetComponent<TextMeshProUGUI>();
            if (timerSystem == null)
            {
                timerSystem = Object.FindFirstObjectByType<TimerSystem>();
                if (timerSystem == null)
                {
                    SafetyLog.Error("TimerUI: No TimerSystem found!", this);
                    enabled = false;
                    return;
                }
            }

            timerSystem.onTimeUpdated.AddListener(UpdateTimeDisplay);
            timerSystem.onTimerCompleted.AddListener(OnTimerCompleted);
            timerSystem.onTimerTimeout.AddListener(OnTimerTimeout);
            _timerText.text = "--:--";
            _timerText.color = Color.white;

            EventBus.Instance.onSessionPaused.AddListener(OnSessionPaused);
            EventBus.Instance.onSessionResumed.AddListener(OnSessionResumed);
        }

        private void OnDestroy()
        {
            if (timerSystem != null)
            {
                timerSystem.onTimeUpdated.RemoveListener(UpdateTimeDisplay);
                timerSystem.onTimerCompleted.RemoveListener(OnTimerCompleted);
                timerSystem.onTimerTimeout.RemoveListener(OnTimerTimeout);
            }

            if (EventBus.Instance != null)
            {
                EventBus.Instance.onSessionPaused.RemoveListener(OnSessionPaused);
                EventBus.Instance.onSessionResumed.RemoveListener(OnSessionResumed);
            }
        }

        private void UpdateTimeDisplay(float seconds)
        {
            bool countingUp = timerSystem != null && timerSystem.IsCountingUp;
            ApplyLabel(countingUp);

            // A group with no time limit reaches zero at the start, not at a missed deadline:
            // the red "esgotado" state must never fire for it.
            if (countingUp || seconds > 0f)
            {
                WriteClock(seconds, Color.white);
                return;
            }

            _lastDisplayedSecond = -1;
            _timerText.text = "00:00";
            _timerText.color = Color.red;
        }

        private void WriteClock(float seconds, Color color)
        {
            var totalSeconds = Mathf.FloorToInt(seconds);
            if (totalSeconds == _lastDisplayedSecond) return;

            _lastDisplayedSecond = totalSeconds;
            _timerText.text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
            _timerText.color = color;
        }

        private void ApplyLabel(bool countingUp)
        {
            if (timerLabel == null || (_labelApplied && _labelIsStopwatch == countingUp)) return;

            _labelApplied = true;
            _labelIsStopwatch = countingUp;
            timerLabel.text = countingUp ? stopwatchLabel : countdownLabel;
        }

        private void OnTimerCompleted(float elapsedTime)
        {
            _lastDisplayedSecond = -1;
            int minutes = Mathf.FloorToInt(elapsedTime / 60F);
            int seconds = Mathf.FloorToInt(elapsedTime % 60);
            _timerText.text = $"{minutes:00}:{seconds:00}";
            _timerText.color = Color.green;
        }

        private void OnTimerTimeout()
        {
            _lastDisplayedSecond = -1;
            _timerText.color = Color.red;
        }

        private void OnSessionPaused(SessionPausedEventArgs obj)
        {
            _timerText.color = Color.yellow;
        }

        private void OnSessionResumed(SessionResumedEventArgs obj)
        {
            _timerText.color = Color.white;
        }
    }
}
