using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Represents a single entry in the task list. Shows order number, task name, highlight state, and completion icon.
/// </summary>
public class TaskEntryUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text orderText;         // e.g. "1."
    [SerializeField] private TMP_Text nameText;          // e.g. "Put On Helmet"
    [SerializeField] private Image backgroundImage;      // to highlight current task
    [SerializeField] private Image stateIcon;            // icon showing completion state

    [Header("State Icons")]
    [Tooltip("Icon when task has not yet been completed")]           
    [SerializeField] private Sprite notCompletedSprite;
    [Tooltip("Icon when task completed successfully")]             
    [SerializeField] private Sprite successSprite;
    [Tooltip("Icon when task completed with failure")]              
    [SerializeField] private Sprite failureSprite;

    public Color normalColor = new Color(1, 1, 1, 0.2f);
    public Color highlightColor = new Color(0.2f, 0.6f, 1f, 0.5f);

    /// <summary>
    /// Initialize order number and name, and set default "not completed" icon.
    /// </summary>
    public void Setup(int order, string taskName)
    {
        orderText.text = $"{order}.";
        nameText.text = taskName;
        SetHighlighted(false);
        SetState(TaskState.NotCompleted);
    }

    /// <summary>
    /// Toggle background highlight to show current task.
    /// </summary>
    public void SetHighlighted(bool isCurrent)
    {
        if (backgroundImage == null) return;
        backgroundImage.color = isCurrent ? highlightColor : normalColor;
    }

    /// <summary>
    /// Update the icon to reflect completion state.
    /// </summary>
    public void SetState(TaskState state)
    {
        if (stateIcon == null) return;

        switch (state)
        {
            case TaskState.NotCompleted:
                stateIcon.sprite = notCompletedSprite;
                break;
            case TaskState.Success:
                stateIcon.sprite = successSprite;
                break;
            case TaskState.Failure:
                stateIcon.sprite = failureSprite;
                break;
        }
    }
}

/// <summary>
/// Defines possible states for a task entry icon.
/// </summary>
public enum TaskState
{
    NotCompleted,
    Success,
    Failure
}
