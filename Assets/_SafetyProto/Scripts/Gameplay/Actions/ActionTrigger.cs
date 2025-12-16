using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.Actions
{
    public class ActionTrigger : MonoBehaviour
    {
        [Header("Action Configuration")]
        [SerializeField] private ActionDefinition action;
        [SerializeField] private string actionIdOverride = string.Empty;
        [SerializeField] private string sourceIdOverride = string.Empty;
        [SerializeField] private string context = string.Empty;
        public int interactorId = 0; // 0 for player, could be specific for multi-user or hands

        public void TriggerAction()
        {
            if (!this.IsEventBusReady())
            {
                return;
            }

            var actionId = ResolveActionId();
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
            if (action != null && !string.IsNullOrWhiteSpace(action.ActionId))
            {
                actionIdOverride = action.ActionId;
            }
            else if (!string.IsNullOrEmpty(actionIdOverride))
            {
                actionIdOverride = actionIdOverride.Trim();
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

        private string ResolveActionId()
        {
            if (action != null && !string.IsNullOrWhiteSpace(action.ActionId))
            {
                actionIdOverride = action.ActionId;
                return action.ActionId;
            }

            return string.IsNullOrWhiteSpace(actionIdOverride) ? string.Empty : actionIdOverride.Trim();
        }
    }
}
