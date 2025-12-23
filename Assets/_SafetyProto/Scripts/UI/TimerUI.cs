using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Gameplay.Task;
using SafetyProto.Core.Logging;
using UnityEngine;
using TMPro;

namespace SafetyProto.UI
{
    public class TimerUI : MonoBehaviour
    {
        [Tooltip("Assign your TimerSystem here (not EventBus).")]
        public TimerSystem timerSystem;
        private TextMeshProUGUI _timerText;
        private int _lastDisplayedSecond = -1;
        private bool _lastPauseState;
        private string _baseLabel = string.Empty;

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
            _timerText.text = "Time: --:--";
            _timerText.color = Color.white;
            _baseLabel = _timerText.text;

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
                if (totalSeconds == _lastDisplayedSecond)
                {
                    return;
                }

                _lastDisplayedSecond = totalSeconds;
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;
                var label = $"Time: {minutes:00}:{seconds:00}";
                SetLabel(label, Color.white);
                _timerText.color = Color.white;
            }
            else
            {
                _lastDisplayedSecond = -1;
                SetLabel("Time Up!", Color.red);
            }
        }

        private void OnTimerCompleted(float elapsedTime)
        {
            _lastDisplayedSecond = -1;
            int minutes = Mathf.FloorToInt(elapsedTime / 60F);
            int seconds = Mathf.FloorToInt(elapsedTime % 60);
            SetLabel($"Completed!\nTime: {minutes:00}:{seconds:00}", Color.green);
        }

        private void OnTimerTimeout()
        {
            _lastDisplayedSecond = -1;
            SetLabel("Time Up!", Color.red);
        }

        private void SetLabel(string text, Color color)
        {
            _baseLabel = text;
            _timerText.text = FormatLabel(text, _lastPauseState);
            _timerText.color = color;
        }

        private string FormatLabel(string text, bool paused)
        {
            return paused ? $"{text} [Paused]" : text;
        }

        private void OnSessionPaused(SessionPausedEventArgs obj)
        {
            _lastPauseState = true;
            if (_timerText != null)
            {
                _timerText.text = FormatLabel(_baseLabel, true);
                _timerText.color = Color.yellow;
            }
        }

        private void OnSessionResumed(SessionResumedEventArgs obj)
        {
            _lastPauseState = false;
            if (_timerText != null)
            {
                _timerText.text = FormatLabel(_baseLabel, false);
            }
        }
    }
}
