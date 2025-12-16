using SafetyProto.Core.Logging;
using SafetyProto.Gameplay.Actions;
using SafetyProto.Gameplay.Events;
using UnityEngine;

namespace SafetyProto.Core.Events
{
    public static class ActionEvents
    {
        public static void Publish(ActionAttemptedEvent payload)
        {
            EventBus.Instance.RaiseActionAttempt(payload);
        }

        public static void PublishActionAttempt(string actionId, string sourceId = null, string context = null, Vector3? position = null, int interactorId = 0)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                SafetyLog.Warning("[ActionEvents] Cannot publish action attempt with empty ActionId.");
                return;
            }

            var normalized = actionId.Trim();
            ActionResolver.TryResolve(normalized, out _);
            Publish(new ActionAttemptedEvent(normalized, sourceId, context, position, interactorId));
        }
    }
}
