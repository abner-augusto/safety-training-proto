using System;
using System.Collections.Generic;
using UnityEngine;

namespace SafetyProto.Gameplay.Actions
{
    [CreateAssetMenu(fileName = "ActionDefinition", menuName = "SafetyProto/Actions/ActionDefinition", order = 0)]
    public class ActionDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string actionId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [TextArea]
        [SerializeField] private string description = string.Empty;
        [SerializeField] private ActionCategory category = ActionCategory.Uncategorized;
        [SerializeField] private string telemetryNameOverride = string.Empty;

        [Header("Metadata")]
        [SerializeField] private string[] tags = Array.Empty<string>();
        [SerializeField] private string[] regulatoryRefs = Array.Empty<string>();
        [SerializeField] private bool isSafetyCritical;
        [SerializeField] private bool isHiddenInUI;

        [Header("Presentation")]
        [SerializeField] private Sprite icon;
        [SerializeField] private Color uiColor = Color.white;

        [Header("Feedback")]
        [SerializeField] private AudioClip sfx;
        [SerializeField] private ScriptableObject haptics;
        [SerializeField] private ScriptableObject feedback;

        [Header("Tuning")]
        [SerializeField] private float cooldownSeconds;
        [SerializeField] private float expectedDurationSeconds;
        [SerializeField] private int baseScore;
        [SerializeField] private int severity;

        public string ActionId => actionId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? actionId : displayName;
        public string Description => description;
        public ActionCategory Category => category;
        public string TelemetryName => string.IsNullOrWhiteSpace(telemetryNameOverride) ? actionId : telemetryNameOverride;
        public IReadOnlyList<string> Tags => tags ?? Array.Empty<string>();
        public IReadOnlyList<string> RegulatoryRefs => regulatoryRefs ?? Array.Empty<string>();
        public bool IsSafetyCritical => isSafetyCritical;
        public bool IsHiddenInUI => isHiddenInUI;
        public Sprite Icon => icon;
        public Color UIColor => uiColor;
        public AudioClip Sfx => sfx;
        public ScriptableObject Haptics => haptics;
        public ScriptableObject Feedback => feedback;
        public float CooldownSeconds => cooldownSeconds;
        public float ExpectedDurationSeconds => expectedDurationSeconds;
        public int BaseScore => baseScore;
        public int Severity => severity;

        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(actionId))
            {
                actionId = actionId.Trim();
            }

            if (!string.IsNullOrEmpty(displayName))
            {
                displayName = displayName.Trim();
            }

            if (!string.IsNullOrEmpty(telemetryNameOverride))
            {
                telemetryNameOverride = telemetryNameOverride.Trim();
            }
        }

        [Serializable]
        public enum ActionCategory
        {
            Uncategorized = 0,
            TaskStep = 1,
            Safety = 2,
            Equipment = 3,
            Communication = 4
        }
    }
}
