using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Actions
{
    public class ActionTrigger : MonoBehaviour
    {
        [Header("Action Configuration")]
        [SerializeField] private string actionId = string.Empty;
        [SerializeField] private string sourceIdOverride = string.Empty;
        [SerializeField] private string context = string.Empty;
        public int interactorId = 0; // 0 for player, could be specific for multi-user or hands

        public void TriggerAction()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            var actionId = GetConfiguredActionId();
            if (string.IsNullOrEmpty(actionId))
            {
                SafetyLog.Warning($"ActionTrigger on {gameObject.name} has no ActionId configured. No event fired.", this);
                return;
            }

            var sourceId = string.IsNullOrWhiteSpace(sourceIdOverride) ? gameObject.name : sourceIdOverride.Trim();
            ActionEvents.PublishActionAttempt(
                actionId,
                sourceId,
                string.IsNullOrWhiteSpace(context) ? null : context.Trim(),
                transform.position,
                interactorId);

            SafetyLog.Info($"ActionTrigger on {gameObject.name} Fired Action: {actionId}", this);
        }

        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(actionId))
            {
                actionId = actionId.Trim();
            }

            if (!string.IsNullOrEmpty(sourceIdOverride))
            {
                sourceIdOverride = sourceIdOverride.Trim();
            }

            if (!string.IsNullOrEmpty(context))
            {
                context = context.Trim();
            }
        }

        private string GetConfiguredActionId()
        {
            return string.IsNullOrWhiteSpace(actionId) ? string.Empty : actionId.Trim();
        }
    }
}
