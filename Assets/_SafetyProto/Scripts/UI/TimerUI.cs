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
        }

        private void OnDestroy()
        {
            if (timerSystem != null)
            {
                timerSystem.onTimeUpdated.RemoveListener(UpdateTimeDisplay);
                timerSystem.onTimerCompleted.RemoveListener(OnTimerCompleted);
                timerSystem.onTimerTimeout.RemoveListener(OnTimerTimeout);
            }
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
                _timerText.text = $"Time: {minutes:00}:{seconds:00}";
                _timerText.color = Color.white;
            }
            else
            {
                _lastDisplayedSecond = -1;
                _timerText.text = "Time Up!";
                _timerText.color = Color.red;
            }
        }

        private void OnTimerCompleted(float elapsedTime)
        {
            _lastDisplayedSecond = -1;
            int minutes = Mathf.FloorToInt(elapsedTime / 60F);
            int seconds = Mathf.FloorToInt(elapsedTime % 60);
            _timerText.text = $"Completed!\nTime: {minutes:00}:{seconds:00}";
            _timerText.color = Color.green;
        }

        private void OnTimerTimeout()
        {
            _lastDisplayedSecond = -1;
            _timerText.text = "Time Up!";
            _timerText.color = Color.red;
        }
    }
}
