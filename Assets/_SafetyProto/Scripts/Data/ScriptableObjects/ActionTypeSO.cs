using System;
using System.Collections.Generic;
using UnityEngine;

namespace SafetyProto.Data.ScriptableObjects
{
    [CreateAssetMenu(fileName = "ActionType", menuName = "SafetyProto/Actions/ActionType", order = 0)]
    public class ActionTypeSO : ScriptableObject
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

        public override string ToString() => DisplayName;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                actionId = GenerateIdFromName(displayName);
            }
            else
            {
                actionId = SanitizeId(actionId);
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

        private static string GenerateIdFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return SanitizeId(name);
        }

        private static string SanitizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim().ToLowerInvariant();
            Span<char> buffer = stackalloc char[value.Length];
            var length = 0;
            foreach (var c in value)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    buffer[length++] = c;
                }
                else if (c == '-' || c == '_' || c == '.')
                {
                    buffer[length++] = c;
                }
                else if (char.IsWhiteSpace(c))
                {
                    buffer[length++] = '_';
                }
            }

            return new string(buffer.Slice(0, length));
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
