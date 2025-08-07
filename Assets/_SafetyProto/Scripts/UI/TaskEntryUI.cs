using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Represents a single entry in the task list. Shows order number, task name, highlight state, completion icon,
/// and auxiliary sprites for "failed once" and "PPE missing" statuses.
/// </summary>
public class TaskEntryUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text orderText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image stateIcon;

    [Header("State Sprites")]
    [Tooltip("Sprite when task has not yet been started or is in progress.")]
    [SerializeField] private Sprite pendingSprite;
    [Tooltip("Sprite when task completed successfully.")]
    [SerializeField] private Sprite successSprite;
    [Tooltip("Sprite when task completed with failure.")]
    [SerializeField] private Sprite failureSprite;
    [Tooltip("Sprite when task succeeded but PPE was missing (combined state).")]
    [SerializeField] private Sprite successWithoutPpeSprite;

    [Header("Penalty Sprites")]
    [Tooltip("Sprite used when task failed once.")]
    [SerializeField] private Sprite failedOnceSprite;
    [Tooltip("Sprite used when task was missing PPE once.")]
    [SerializeField] private Sprite ppeMissingSprite;

    [Header("Penalty Icon Display")]
    [Tooltip("UI image used to show the failed once sprite.")]
    [SerializeField] private Image failedOnceIcon;
    [Tooltip("UI image used to show the PPE missing sprite.")]
    [SerializeField] private Image ppeMissingIcon;

    [Header("Colors")]
    public Color normalColor = new Color(1f, 1f, 1f, 0.2f);
    public Color highlightColor = new Color(0.2f, 0.6f, 1f, 0.5f);

    /// <summary>
    /// Initializes the static text for the task entry.
    /// </summary>
    public void Setup(int order, string taskName)
    {
        if (orderText != null)
            orderText.text = $"{order}.";
        if (nameText != null)
            nameText.text = taskName;

        if (failedOnceIcon != null)
            failedOnceIcon.gameObject.SetActive(false);
        if (ppeMissingIcon != null)
            ppeMissingIcon.gameObject.SetActive(false);

        UpdateState(TaskState.NotStarted);
    }

    /// <summary>
    /// Updates the background color, main state icon, and penalty icons based on the task's current state.
    /// </summary>
    public void UpdateState(TaskState newState)
    {
        if (stateIcon == null || backgroundImage == null)
            return;

        // Determine penalty icon visibility
        bool ppeMissing = newState == TaskState.CompletedSuccessButUnsafe;
        bool failedOnce = newState == TaskState.CompletedFailure;

        if (ppeMissingIcon != null)
        {
            ppeMissingIcon.sprite = ppeMissingSprite;
            ppeMissingIcon.gameObject.SetActive(ppeMissing);
        }

        if (failedOnceIcon != null)
        {
            failedOnceIcon.sprite = failedOnceSprite;
            failedOnceIcon.gameObject.SetActive(failedOnce);
        }

        // Set the main state icon
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

            case TaskState.CompletedSuccessButUnsafe:
                stateIcon.sprite = successWithoutPpeSprite;
                backgroundImage.color = normalColor;
                break;

            case TaskState.CompletedFailure:
                stateIcon.sprite = failureSprite;
                backgroundImage.color = normalColor;
                break;

            default:
                backgroundImage.color = normalColor;
                break;
        }
    }
}
