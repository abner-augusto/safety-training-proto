#nullable enable
namespace SafetyProto.Core.Events
{
    /// <summary>
    /// The four correlation fields every bus payload carries. Implemented explicitly by each
    /// payload struct so <see cref="EventMetadata.Stamp{T}"/> can fill them without a
    /// per-type list — a payload that forgets to implement this is caught by
    /// <c>EventMetadataTests</c>, not by an analyst finding empty session ids in a log.
    /// </summary>
    public interface IEventPayload
    {
        string SessionId { get; }
        string PlayerId { get; }
        string ScenarioId { get; }
        long TimestampMs { get; }

        void SetMetadata(string sessionId, string playerId, string scenarioId, long timestampMs);
    }
}
