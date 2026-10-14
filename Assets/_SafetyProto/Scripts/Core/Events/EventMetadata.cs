#nullable enable
using SafetyProto.Core;

namespace SafetyProto.Core.Events
{
    /// <summary>Shared metadata stamping contract used by Unity, CLI, and test event buses.</summary>
    public static class EventMetadata
    {
        public static T Stamp<T>(T payload)
        {
            if (payload is IEventPayload stampable)
            {
                stampable.SetMetadata(
                    EventContext.CurrentSessionId ?? string.Empty,
                    EventContext.CurrentPlayerId ?? string.Empty,
                    EventContext.CurrentScenarioId ?? string.Empty,
                    EventContext.NowUnixMs());
                return (T)stampable;
            }

            return payload;
        }
    }
}
