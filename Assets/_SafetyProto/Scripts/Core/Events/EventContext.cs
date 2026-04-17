#nullable enable
using System;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// Tracks the current logical context for raised events (who, where, when).
    /// </summary>
    public static class EventContext
    {
        public static string? CurrentSessionId { get; private set; }
        public static string? CurrentPlayerId { get; private set; }
        public static string? CurrentScenarioId { get; private set; }

        public static void StartSession(string sessionId, string playerId, string scenarioId)
        {
            CurrentSessionId = sessionId;
            CurrentPlayerId = playerId;
            CurrentScenarioId = scenarioId;
        }

        public static void Clear()
        {
            CurrentSessionId = null;
            CurrentPlayerId = null;
            CurrentScenarioId = null;
        }

        public static long NowUnixMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
