using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Actions
{
    /// <summary>
    /// Helper component that emits action attempts with optional context metadata.
    /// Useful for wiring triggers, interactables, or UI buttons without custom scripts.
    /// </summary>
    public class ActionEmitter : MonoBehaviour
    {
        [Header("Action")]
        [SerializeField] private string actionId = string.Empty;

        [Header("Metadata")]
        [SerializeField] private string sourceId = string.Empty;
        [SerializeField] private string context = string.Empty;
        [SerializeField] private int interactorId;

        [Header("Behavior")]
        [SerializeField] private bool autoEmitOnEnable;
        [SerializeField] private float debounceSeconds;

        private float _lastEmitTime = -Mathf.Infinity;

        private void OnEnable()
        {
            if (autoEmitOnEnable)
            {
                Emit();
            }
        }

        public string ConfiguredActionId => GetConfiguredActionId();

        public void Emit()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            var actionId = GetConfiguredActionId();
            if (string.IsNullOrEmpty(actionId))
            {
                SafetyLog.Warning($"[ActionEmitter] No ActionId configured on {name}. Skipping emit.", this);
                return;
            }

            if (!CanEmit())
            {
                return;
            }

            var position = transform != null ? transform.position : (Vector3?)null;
            var resolvedSource = string.IsNullOrWhiteSpace(sourceId) ? gameObject.name : sourceId.Trim();
            var resolvedContext = string.IsNullOrWhiteSpace(context) ? null : context.Trim();

            ActionEvents.PublishActionAttempt(actionId, resolvedSource, resolvedContext, position, interactorId);
            _lastEmitTime = Time.time;
        }

        private bool CanEmit()
        {
            if (debounceSeconds <= 0f)
            {
                return true;
            }

            return Time.time >= _lastEmitTime + debounceSeconds;
        }

        private string GetConfiguredActionId()
        {
            return string.IsNullOrWhiteSpace(actionId) ? string.Empty : actionId.Trim();
        }

        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(actionId))
            {
                actionId = actionId.Trim();
            }

            if (!string.IsNullOrEmpty(sourceId))
            {
                sourceId = sourceId.Trim();
            }

            if (!string.IsNullOrEmpty(context))
            {
                context = context.Trim();
            }

            debounceSeconds = Mathf.Max(0f, debounceSeconds);
        }
    }
}
