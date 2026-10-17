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

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            _scoreText = GetComponent<TextMeshProUGUI>();
            EventBus.Instance.Subscribe<ScoreChangedEventArgs>(OnScoreChanged);
            UpdateScoreDisplay(0);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.Unsubscribe<ScoreChangedEventArgs>(OnScoreChanged);
        }

        private void OnScoreChanged(ScoreChangedEventArgs args) => UpdateScoreDisplay(args.TotalScore);

        private void UpdateScoreDisplay(int score)
        {
            if (_scoreText != null) _scoreText.text = $"{Mathf.Max(0, score)}";
        }
    }
}
