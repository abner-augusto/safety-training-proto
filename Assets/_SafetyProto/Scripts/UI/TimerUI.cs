using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Gameplay.Task;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    public class TimerUI : MonoBehaviour
    {
        [Tooltip("Assign your TimerSystem here (not EventBus).")]
        public TimerSystem timerSystem;

        private TextMeshProUGUI _timerText;
        private int _lastDisplayedSecond = -1;
        private Color _currentColor = Color.white;

        private void Start()
        {
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

            EventBus.OnSessionPausedCSharp += OnSessionPaused;
            EventBus.OnSessionResumedCSharp += OnSessionResumed;
        }

        private void OnDestroy()
        {
            if (timerSystem != null)
            {
                timerSystem.onTimeUpdated.RemoveListener(UpdateTimeDisplay);
                timerSystem.onTimerCompleted.RemoveListener(OnTimerCompleted);
                timerSystem.onTimerTimeout.RemoveListener(OnTimerTimeout);
            }

            EventBus.OnSessionPausedCSharp -= OnSessionPaused;
            EventBus.OnSessionResumedCSharp -= OnSessionResumed;
        }

        private void UpdateTimeDisplay(float timeRemaining)
        {
            if (timeRemaining > 0f)
            {
                var totalSeconds = Mathf.FloorToInt(timeRemaining);
                if (totalSeconds == _lastDisplayedSecond) return;

                _lastDisplayedSecond = totalSeconds;
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;
                _timerText.text = $"{minutes:00}:{seconds:00}";
                _timerText.color = Color.white;
            }
            else
            {
                _lastDisplayedSecond = -1;
                _timerText.text = "00:00";
                _timerText.color = Color.red;
            }
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
            _timerText.color = _currentColor;
        }
    }
}
