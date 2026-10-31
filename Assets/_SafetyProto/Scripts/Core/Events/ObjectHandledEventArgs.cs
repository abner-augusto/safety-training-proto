#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// A hand took hold of an interactive object (PPE, decoy, scaffold piece, tool, lanyard). The
    /// participant's next action, as the feedback popups and the guided hints read it, and one grab
    /// attempt in the session log.
    /// </summary>
    [Serializable]
    public struct ObjectGrabbedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        /// <summary>Scene name of the object.</summary>
        public string Target;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }
    }

    /// <summary>
    /// The last hand let go of an interactive object. <see cref="Secured"/> tells a placement (worn,
    /// installed, anchored) from a drop, so the log can count involuntary drops per object.
    /// </summary>
    [Serializable]
    public struct ObjectReleasedEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        /// <summary>Scene name of the object.</summary>
        public string Target;
        /// <summary>Seconds between the grab and this release.</summary>
        public float HeldSeconds;
        /// <summary>The release left the object worn, installed or anchored.</summary>
        public bool Secured;

        string IEventPayload.SessionId => SessionId;
        string IEventPayload.PlayerId => PlayerId;
        string IEventPayload.ScenarioId => ScenarioId;
        long IEventPayload.TimestampMs => TimestampMs;

        void IEventPayload.SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            ScenarioId = scenarioId;
            TimestampMs = timestampMs;
        }
    }
}
