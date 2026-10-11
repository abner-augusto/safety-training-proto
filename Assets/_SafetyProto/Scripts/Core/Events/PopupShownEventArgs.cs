#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// The shared popup panel just became visible. Published by the UI layer so a system that
    /// must react to the panel itself — rather than to the session pause it also raises — can
    /// do so without taking a dependency on the UI assembly. The popup camera's hand overlay
    /// listens to this pair to render the hands above the panel while it is up.
    /// </summary>
    [Serializable]
    public struct PopupShownEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

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
