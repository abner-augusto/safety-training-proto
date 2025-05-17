using UnityEngine;
using TMPro; // For TextMeshPro

[RequireComponent(typeof(TextMeshProUGUI))]
public class ScoreHUD : MonoBehaviour
{
    [Tooltip("Assign your EventBus ScriptableObject asset here. Can be auto-found if only one.")]
    public EventBus eventBus;
    private TextMeshProUGUI _scoreText;

    void Start()
    {
        _scoreText = GetComponent<TextMeshProUGUI>();
        if (eventBus == null)
        {
            // Attempt to find it if not assigned - assumes a single EventBus SO is loaded and assigned as EventBus.Instance
            eventBus = EventBus.Instance; // This relies on EventBus.Instance being set correctly
            if (eventBus == null)
            {
                Debug.LogError("EventBus not assigned or found for ScoreHUD!", this);
                enabled = false;
                return;
            }
        }

        eventBus.OnScoreChanged.AddListener(UpdateScoreDisplay);
        // Initialize display, e.g. get current score if ScoreManager exists
        ScoreManager sm = ScoreManager.Instance; // Assumes ScoreManager is a singleton
        if (sm != null)
        {
            UpdateScoreDisplay(new ScoreChangedEventArgs(sm.GetCurrentScore(), 0));
        }
        else
        {
            _scoreText.text = "Score: 0";
        }
    }

    private void OnDestroy()
    {
        if (eventBus != null)
        {
            eventBus.OnScoreChanged.RemoveListener(UpdateScoreDisplay);
        }
    }

    private void UpdateScoreDisplay(ScoreChangedEventArgs args)
    {
        if (_scoreText != null)
        {
            _scoreText.text = $"Score: {args.TotalScore}";
        }
    }
}