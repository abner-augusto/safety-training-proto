using SafetyProto.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    /// <summary>How strongly a pending task row calls for attention.</summary>
    public enum TaskHighlight
    {
        None,
        /// <summary>Available now but not the one to do first: a free-order sibling.</summary>
        Secondary,
        /// <summary>The task to do next: the current sequential task or a pending prerequisite.</summary>
        Primary
    }

    /// <summary>
    /// Represents a single entry in the task list. Shows order number, task name, highlight state, completion icon,
    /// and auxiliary sprites for "failed once" and "PPE missing" statuses.
    /// </summary>
    public class TaskEntryUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text orderText;
        [SerializeField] private TMP_Text nameText;
        [Tooltip("Disc behind the state icon; tinted with highlightColor while the task is active.")]
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image stateIcon;
        [Tooltip("Rounded row background that pulses while the task is active.")]
        [SerializeField] private Image pillImage;

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

        [Header("Active Pulse")]
        public Color pillColor = new Color(0.27f, 0.31f, 0.37f, 1f);
        [Tooltip("Peak of the pulse. Keep it dark enough for the white task name to stay legible.")]
        public Color pillPulseColor = new Color(0.78f, 0.5f, 0.12f, 1f);
        [Tooltip("Peak of the Secondary pulse. Stays close to pillColor so a whole free-order list pulsing does not compete with the text or a Primary row.")]
        public Color pillSoftPulseColor = new Color(0.4f, 0.37f, 0.32f, 1f);
        [Tooltip("Seconds per pulse cycle. The panel's overlay canvas redraws every few frames, so short periods strobe.")]
        [SerializeField] private float pulsePeriod = 1.6f;

        private TaskHighlight _highlight;

        private void Update()
        {
            if (_highlight == TaskHighlight.None || pillImage == null) return;

            // A shared clock, not a per-row timer, keeps every pulsing row in phase.
            float wave = 0.5f - 0.5f * Mathf.Cos(Time.time * (2f * Mathf.PI / pulsePeriod));
            var peak = _highlight == TaskHighlight.Primary ? pillPulseColor : pillSoftPulseColor;
            pillImage.color = Color.Lerp(pillColor, peak, wave);
        }

        /// <summary>Sets how a pending row pulses; <see cref="UpdateState"/> overrides it.</summary>
        public void SetHighlight(TaskHighlight highlight)
        {
            _highlight = highlight;
            if (highlight == TaskHighlight.None && pillImage != null)
                pillImage.color = pillColor;
            if (backgroundImage != null)
                backgroundImage.color = highlight == TaskHighlight.Primary ? highlightColor : normalColor;
        }

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

        public void UpdateState(TaskState newState)
        {
            _highlight = newState == TaskState.InProgress ? TaskHighlight.Primary : TaskHighlight.None;
            if (_highlight == TaskHighlight.None && pillImage != null)
                pillImage.color = pillColor;

            if (stateIcon == null || backgroundImage == null)
                return;

            bool ppeMissing = newState == TaskState.CompletedSuccessButUnsafe;
            bool notPerformed = newState == TaskState.NotPerformed;

            if (ppeMissingIcon != null)
            {
                ppeMissingIcon.sprite = ppeMissingSprite;
                ppeMissingIcon.gameObject.SetActive(ppeMissing);
            }

            if (failedOnceIcon != null)
            {
                failedOnceIcon.sprite = failedOnceSprite;
                failedOnceIcon.gameObject.SetActive(notPerformed);
            }

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

                case TaskState.NotPerformed:
                    stateIcon.sprite = failureSprite;
                    backgroundImage.color = normalColor;
                    break;

                default:
                    backgroundImage.color = normalColor;
                    break;
            }
        }
    }
}
