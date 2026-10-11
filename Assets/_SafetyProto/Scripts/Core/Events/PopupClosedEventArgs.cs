#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// The shared popup panel just went away — dismissed by the participant, by its action
    /// button, or by its auto-close timer. Published by the UI layer so gameplay objects can
    /// wait for the participant to have read a warning before changing the world under them,
    /// without taking a dependency on the UI assembly.
    /// </summary>
    [Serializable]
    public struct PopupClosedEventArgs : IEventPayload
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

        /// <summary>Echoes the violation code that opened the popup (see
        /// <see cref="ActionRefusedEventArgs.ReasonCode"/>), so a piece waiting behind a warning
        /// can tell its own refusal apart from an unrelated popup. Empty for popups opened for
        /// anything else.</summary>
        public string ReasonCode;
    }
}
