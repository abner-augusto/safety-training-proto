// Assets/_SafetyProto/Tests/Editor/Support/FakeDashboardHost.cs
using System.Collections.Generic;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Domain.Scoring;

namespace SafetyProto.Tests.Editor.Support
{
    /// <summary>Records every relay Broadcast and serves controllable ambient state.</summary>
    public sealed class FakeDashboardHost : IDashboardHost
    {
        public readonly List<(string eventType, object payload)> Broadcasts = new List<(string, object)>();
        public readonly List<ITaskGroup> Registered = new List<ITaskGroup>();
        private readonly List<ITaskGroup> _knownGroups = new List<ITaskGroup>();

        public bool VerboseEvents { get; set; } = true;
        public ScoringConfig Scoring { get; set; } = ScoringConfig.Default;
        public IReadOnlyList<ITaskGroup> KnownGroups => _knownGroups;
        public int ManifestBuilds { get; private set; }

        public void SeedKnownGroup(ITaskGroup group) => _knownGroups.Add(group);

        public void RegisterKnownGroup(ITaskGroup group)
        {
            Registered.Add(group);
            if (!_knownGroups.Contains(group)) _knownGroups.Add(group);
        }

        // Identity clock so tests can assert timestamp pass-through exactly.
        public long ResolveTimestamp(long timestampMs) => timestampMs;

        public SessionManifestDto BuildSessionManifest(string sessionId)
        {
            ManifestBuilds++;
            return new SessionManifestDto { sessionId = sessionId, tasks = System.Array.Empty<TaskManifestItemDto>() };
        }

        public void Broadcast<T>(string eventType, T payload) => Broadcasts.Add((eventType, payload!));

        public T Last<T>(string eventType)
        {
            for (int i = Broadcasts.Count - 1; i >= 0; i--)
                if (Broadcasts[i].eventType == eventType && Broadcasts[i].payload is T match) return match;
            throw new KeyNotFoundException($"No broadcast of type {eventType} with payload {typeof(T).Name}");
        }

        public int Count(string eventType)
        {
            int n = 0;
            foreach (var b in Broadcasts) if (b.eventType == eventType) n++;
            return n;
        }
    }
}
