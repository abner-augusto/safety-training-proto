using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Actions;
using SafetyProto.Core.Events;
using UnityEngine;

namespace SafetyProto.Core.Events
{
    public static class ActionEvents
    {
        public static void Publish(ActionAttemptedEvent payload)
            => EventBus.Instance.RaiseActionAttempt(payload);

        /// <summary>
        /// Unity-friendly overload: accepts Vector3? and converts to the engine-independent
        /// tuple form before publishing.
        /// </summary>
        public static void PublishActionAttempt(
            string actionId,
            string sourceId = null,
            string context = null,
            Vector3? position = null,
            int interactorId = 0)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                SafetyLog.Warning("[ActionEvents] Cannot publish action attempt with empty ActionId.");
                return;
            }

            var normalized = actionId.Trim();
            ActionResolver.TryResolve(normalized, out _);

            (float X, float Y, float Z)? tuplePos = position.HasValue
                ? (position.Value.x, position.Value.y, position.Value.z)
                : null;

            Publish(new ActionAttemptedEvent(normalized, sourceId, context, tuplePos, interactorId));
        }
    }
}
