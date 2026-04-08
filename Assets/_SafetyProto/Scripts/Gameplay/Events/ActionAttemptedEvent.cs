using System;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.Actions;
using UnityEngine;

namespace SafetyProto.Gameplay.Events
{
    /// <summary>
    /// Payload emitted whenever an action attempt occurs in gameplay.
    /// Captures canonical action IDs plus optional spatial/source context.
    /// </summary>
    [Serializable]
    public struct ActionAttemptedEvent
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public string ActionId;
        public string SourceId;
        public string Context;
        public Vector3? Position;
        public int InteractorId;
        public float Time;

        public ActionAttemptedEvent(string actionId, string sourceId = null, string context = null, Vector3? position = null, int interactorId = 0)
        {
            SessionId = string.Empty;
            PlayerId = string.Empty;
            ScenarioId = string.Empty;
            TimestampMs = 0L;

            ActionId = actionId ?? string.Empty;
            SourceId = sourceId;
            Context = context;
            Position = position;
            InteractorId = interactorId;
            Time = UnityEngine.Time.time;
        }

        public ActionTypeSO ResolveDefinition()
        {
            return string.IsNullOrEmpty(ActionId) ? null : ActionResolver.Resolve(ActionId);
        }
    }
}
