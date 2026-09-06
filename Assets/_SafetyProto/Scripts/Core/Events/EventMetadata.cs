#nullable enable
using SafetyProto.Core;

namespace SafetyProto.Core.Events
{
    /// <summary>Shared metadata stamping contract used by Unity, CLI, and test event buses.</summary>
    public static class EventMetadata
    {
        public static void StampFields(ref string sessionId, ref string playerId, ref string scenarioId, ref long timestampMs)
        {
            sessionId = EventContext.CurrentSessionId ?? string.Empty;
            playerId = EventContext.CurrentPlayerId ?? string.Empty;
            scenarioId = EventContext.CurrentScenarioId ?? string.Empty;
            timestampMs = EventContext.NowUnixMs();
        }

        public static T Stamp<T>(T payload)
        {
            object? boxed = payload;
            if (boxed == null) return payload;

            switch (boxed)
            {
                case SessionStartedEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case SessionPausedEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case SessionResumedEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case SessionEndedEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case SessionCompletedEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case ActionAttemptedEvent value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case ActionRefusedEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case PopupClosedEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case PPEStateChangedEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case TaskEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case TaskGroupEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case ScoreChangedEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case SafetyViolationEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case CriticalSafetyFailureEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
                case SafetyErrorEventArgs value: StampFields(ref value.SessionId, ref value.PlayerId, ref value.ScenarioId, ref value.TimestampMs); boxed = value; break;
            }

            return (T)boxed;
        }
    }
}
