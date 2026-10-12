#nullable enable
using System;

namespace SafetyProto.Domain.Sessions
{
    /// <summary>
    /// The shared part of every per-session file name, "yyyyMMdd_HHmmss_sid8": the session's UTC
    /// start plus the first eight characters of its id. The session log and the perf CSV both
    /// derive their names from it, so the two files of one session always pair up.
    /// </summary>
    public static class SessionFileStem
    {
        public static string Of(long sessionStartUnixMs, string? sessionId)
        {
            var startMs = sessionStartUnixMs > 0 ? sessionStartUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(startMs).UtcDateTime.ToString("yyyyMMdd_HHmmss");
            return string.IsNullOrEmpty(sessionId)
                ? timestamp
                : timestamp + "_" + sessionId!.Substring(0, Math.Min(8, sessionId.Length));
        }
    }
}
