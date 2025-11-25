using SafetyProto.Core.Interfaces;
using SafetyProto.Data.ScriptableObjects;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class ScoreHUD : MonoBehaviour
    {
        [Tooltip("Link the ScoreService ScriptableObject.")]
        public ScoreServiceSO scoreServiceAsset;

        private TextMeshProUGUI _scoreText;
        private IScoreService _scoreService;

        public static ScoreHUD Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            _scoreText = GetComponent<TextMeshProUGUI>();

            if (scoreServiceAsset == null)
            {
                Debug.LogError("ScoreHUD: ScoreService asset not assigned.", this);
                enabled = false;
                return;
            }

            _scoreService = scoreServiceAsset.Service;
            _scoreService.ScoreChanged += OnScoreChanged;

            UpdateScoreDisplay(_scoreService.CurrentScore);
        }

        private void OnDestroy()
        {
            if (_scoreService != null)
            {
                _scoreService.ScoreChanged -= OnScoreChanged;
            }
        }

        private void OnScoreChanged(int newScore, int delta, string reason)
        {
            UpdateScoreDisplay(newScore);
        }

        private void UpdateScoreDisplay(int score)
        {
            if (_scoreText != null)
            {
                _scoreText.text = $"Score: {score}";
            }
        }

        public void ShowDelta(int delta, string reason, int totalScore)
        {
            Debug.Log($"[HUD] {reason}: {(delta >= 0 ? "+" : "")}{delta} (Total: {totalScore})");
            UpdateScoreDisplay(totalScore);
        }
    }
}
