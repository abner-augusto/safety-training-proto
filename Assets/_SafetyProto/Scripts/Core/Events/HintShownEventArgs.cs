#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// Guided mode helped a participant who made no progress on a task: level 1 made the next object
    /// pulse, level 2 said the step aloud, level 3 alerted the evaluator. The analysis uses the time
    /// to the first hint and the number of hints as covariates; never scored.
    /// </summary>
    [Serializable]
    public struct HintShownEventArgs : IEventPayload
    {
        public string SessionId;
        public string PlayerId;
        public string ScenarioId;
        public long TimestampMs;

        public int Level;
        public string TaskId;
        public string TaskName;
        public float SecondsWithoutProgress;

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
