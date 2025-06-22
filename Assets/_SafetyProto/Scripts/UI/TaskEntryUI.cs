using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Represents a single entry in the task list. Shows order number, task name, highlight state, and completion icon. 
/// </summary>
public class TaskEntryUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text orderText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image stateIcon;

    [Header("State Visuals")]
    [Tooltip("Icon when task has not yet been completed or is in progress.")]
    [SerializeField] private Sprite pendingSprite; // Renamed from notCompletedSprite
    [Tooltip("Icon when task completed successfully.")]
    [SerializeField] private Sprite successSprite;
    [Tooltip("Icon when task completed with failure.")]
    [SerializeField] private Sprite failureSprite;

    [Header("Colors")] public Color normalColor = new Color(1, 1, 1, 0.2f);
    public Color highlightColor = new Color(0.2f, 0.6f, 1f, 0.5f);

    /// <summary>
    /// Initializes the static text for the task entry.
    /// </summary>
    public void Setup(int order, string taskName)
    {
        orderText.text = $"{order}.";
        nameText.text = taskName;
        UpdateState(TaskState.NotStarted); // Set initial state
    }

    /// <summary>
    /// Updates the background color and state icon based on the task's current state.
    /// </summary>
    public void UpdateState(TaskState newState)
    {
        if (stateIcon == null || backgroundImage == null) return;

        switch (newState)
        {
            case TaskState.NotStarted:
                stateIcon.sprite = pendingSprite;
                backgroundImage.color = normalColor;
                break;

            case TaskState.InProgress:
                stateIcon.sprite = pendingSprite;
                backgroundImage.color = highlightColor;
                break;

            case TaskState.CompletedSuccess:
                stateIcon.sprite = successSprite;
                backgroundImage.color = normalColor;
                break;

            case TaskState.CompletedFailure:
                stateIcon.sprite = failureSprite;
                backgroundImage.color = normalColor;
                break;
        }
    }
}