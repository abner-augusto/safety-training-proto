#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>Whether the practice area opened or closed.</summary>
    public enum PracticePhase
    {
        Started,
        Ended
    }

    /// <summary>
    /// The practice area before the training: the first onboarding steps, then the participant
    /// tries the interactions on neutral props, while the session clock stands still. Nothing in
    /// it is scored; the analysis reads how long it lasted, how much of it was free practice, how
    /// it ended and which interactions were tried.
    /// </summary>
    [Serializable]
    public struct PracticeEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public PracticePhase Phase;

        /// <summary>How it ended (<c>continue</c>, <c>skipped</c>, <c>timeout</c>, <c>evaluator</c>); empty on start.</summary>
        public string Reason;

        /// <summary>Seconds spent in the area, onboarding steps included; 0 on start.</summary>
        public float Seconds;

        /// <summary>Seconds of free practice, from the end of the onboarding steps to the close;
        /// 0 on start and when the area closed during the steps. Builds up to 1.2.14 opened the
        /// area after those steps, so their <see cref="Seconds"/> compares with this.</summary>
        public float FreeSeconds;

        /// <summary>Stable ids of the interactions done in the area, in the order first done; null on start.</summary>
        public string[]? Tried;

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
