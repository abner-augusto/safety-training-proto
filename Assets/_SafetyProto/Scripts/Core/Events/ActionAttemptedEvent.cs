#nullable enable
using System;

namespace SafetyProto.Gameplay.Events
{
    /// <summary>
    /// Payload emitted whenever an action attempt occurs in gameplay.
    /// Captures canonical action IDs plus optional spatial/source context.
    ///
    /// Engine-independent: Position is a plain tuple rather than Vector3, and no
    /// Unity-only API is referenced. Use the Unity-side ActionEvents adapter
    /// to convert Vector3 positions into the tuple form.
    /// </summary>
    [Serializable]
    public struct ActionAttemptedEvent
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public string ActionId;
        public string? SourceId;
        public string? Context;

        /// <summary>Spatial position as (X, Y, Z), engine-independent.</summary>
        public (float X, float Y, float Z)? Position;

        public int InteractorId;

        public ActionAttemptedEvent(
            string actionId,
            string? sourceId = null,
            string? context = null,
            (float X, float Y, float Z)? position = null,
            int interactorId = 0)
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
        }
    }
}
