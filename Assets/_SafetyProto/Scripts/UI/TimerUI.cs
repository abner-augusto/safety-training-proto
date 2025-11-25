using SafetyProto.Gameplay.Task;
using UnityEngine;
using TMPro;

namespace SafetyProto.UI
{
    public class TimerUI : MonoBehaviour
    {
        [Tooltip("Assign your TimerSystem here (not EventBus).")]
        public TimerSystem timerSystem;
        private TextMeshProUGUI _timerText;

        private float _lastElapsed;

        private void Start()
        {
            _timerText = GetComponent<TextMeshProUGUI>();
            if (timerSystem == null)
            {
                timerSystem = Object.FindFirstObjectByType<TimerSystem>();
                if (timerSystem == null)
                {
                    Debug.LogError("TimerUI: No TimerSystem found!", this);
                    enabled = false;
                    return;
                }
            }

            timerSystem.onTimeUpdated.AddListener(UpdateTimeDisplay);
            timerSystem.onTimerCompleted.AddListener(OnTimerCompleted);
            timerSystem.onTimerTimeout.AddListener(OnTimerTimeout);

            UpdateTimeDisplay(timerSystem.GetTimeRemaining());
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
            if (timeRemaining > 0)
            {
                int minutes = Mathf.FloorToInt(timeRemaining / 60F);
                int seconds = Mathf.FloorToInt(timeRemaining % 60);
                _timerText.text = $"Time: {minutes:00}:{seconds:00}";
                _timerText.color = Color.white;
            }
            else
            {
                _timerText.text = "Time Up!";
                _timerText.color = Color.red;
            }
        }

        private void OnTimerCompleted(float elapsedTime)
        {
            int minutes = Mathf.FloorToInt(elapsedTime / 60F);
            int seconds = Mathf.FloorToInt(elapsedTime % 60);
            _timerText.text = $"Completed!\nTime: {minutes:00}:{seconds:00}";
            _timerText.color = Color.green;
            _lastElapsed = elapsedTime;
        }

        private void OnTimerTimeout()
        {
            _timerText.text = "Time Up!";
            _timerText.color = Color.red;
        }
    }
}
