using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class ScoreHUD : MonoBehaviour
    {
        private TextMeshProUGUI _scoreText;
        private int _currentScore;

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            _scoreText = GetComponent<TextMeshProUGUI>();

            EventBus.Instance.onScoreChanged.AddListener(OnScoreChanged);
            UpdateScoreDisplay(_currentScore);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onScoreChanged.RemoveListener(OnScoreChanged);
            }
        }

        private void OnScoreChanged(ScoreChangedEventArgs args)
        {
            _currentScore = args.TotalScore;
            UpdateScoreDisplay(_currentScore);
        }

        private void UpdateScoreDisplay(int score)
        {
            if (_scoreText != null)
            {
                _scoreText.text = $"Score: {score}";
            }
        }
    }
}
