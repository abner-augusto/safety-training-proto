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
            EventBus.Instance.onScoreChanged.AddListener(OnScoreChanged);
            UpdateScoreDisplay(0);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.onScoreChanged.RemoveListener(OnScoreChanged);
        }

        private void OnScoreChanged(ScoreChangedEventArgs args) => UpdateScoreDisplay(args.TotalScore);

        private void UpdateScoreDisplay(int score)
        {
            if (_scoreText != null) _scoreText.text = $"Score: {score}";
        }
    }
}
