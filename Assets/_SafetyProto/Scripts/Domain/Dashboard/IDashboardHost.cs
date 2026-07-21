// Assets/_SafetyProto/Scripts/Domain/Dashboard/IDashboardHost.cs
using System.Collections.Generic;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scoring;

namespace SafetyProto.Domain.Dashboard
{
    /// <summary>
    /// The Unity-side surface the pure <see cref="DashboardEventRelay"/> needs: the WS
    /// broadcast sink, the scene-derived ambient state (scoring, known groups), the clock
    /// (ResolveTimestamp), the manifest builder, and the session-log broadcast trigger.
    /// EvaluatorDashboardBootstrap implements this; the relay never touches Unity directly.
    /// </summary>
    public interface IDashboardHost
    {
        bool VerboseEvents { get; }
        ScoringConfig Scoring { get; }
        IReadOnlyList<ITaskGroup> KnownGroups { get; }
        void RegisterKnownGroup(ITaskGroup group);
        long ResolveTimestamp(long timestampMs);
        SessionManifestDto BuildSessionManifest(string sessionId);
        void QueueSessionLogBroadcast(string sessionId, string playerId);
        void Broadcast<T>(string eventType, T payload);
    }
}
