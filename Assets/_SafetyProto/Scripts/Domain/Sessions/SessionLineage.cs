#nullable enable
using SafetyProto.Core;

namespace SafetyProto.Domain.Sessions
{
    /// <summary>A session waiting to start: who, in which mode, and which session it replaces.</summary>
    public sealed class SessionStartRequest
    {
        public SessionStartRequest(string participantId, string source, SessionMode mode,
                                   string previousSessionId = "", int attempt = 1)
        {
            ParticipantId = participantId ?? string.Empty;
            Source = source ?? string.Empty;
            Mode = mode;
            PreviousSessionId = previousSessionId ?? string.Empty;
            Attempt = attempt < 1 ? 1 : attempt;
        }

        public string ParticipantId { get; }
        public string Source { get; }
        public SessionMode Mode { get; }
        /// <summary>The interrupted session this one restarts; empty for a first attempt.</summary>
        public string PreviousSessionId { get; }
        /// <summary>1 for a first attempt, +1 for every interrupted session of the same code it restarts.</summary>
        public int Attempt { get; }

        public SessionStartRequest ChainedTo(string previousSessionId, int attempt) =>
            new SessionStartRequest(ParticipantId, Source, Mode, previousSessionId, attempt);

        public SessionStartRequest Unchained() => new SessionStartRequest(ParticipantId, Source, Mode);
    }

    /// <summary>The session being torn down by a reset, as the reset sees it.</summary>
    public sealed class SessionSnapshot
    {
        public bool Started;
        public bool Completed;
        public string SessionId = string.Empty;
        public string ParticipantId = string.Empty;
        public string Source = string.Empty;
        public SessionMode Mode;
        public int Attempt;
    }

    /// <summary>
    /// Decides what the next scene starts with after a reset. An interrupted session restarts
    /// with the same participant and mode — or the identity the evaluator just set — and is
    /// linked to the session it replaces; attempts count the tries under the same code, so the
    /// analysis can count them. A completed session hands over to the next participant: only an
    /// explicit request starts anything.
    /// </summary>
    public static class SessionLineage
    {
        /// <param name="carried">The start an earlier reset left for this scene, still unconsumed:
        /// a reset that lands during the reload, before the new scene began, must not drop it.</param>
        public static SessionStartRequest? Next(SessionSnapshot current, SessionStartRequest? requested,
                                                SessionStartRequest? carried = null)
        {
            if (!current.Started && carried != null)
                return requested == null ? carried : Supersede(carried, requested);

            bool interrupted = current.Started && !current.Completed;

            // A simulator run restarted for real would start a participant session nobody drives.
            bool restartable = interrupted && current.Source != ParticipantSources.Simulator;
            if (!restartable) return requested?.Unchained();

            var identity = requested
                ?? new SessionStartRequest(current.ParticipantId, current.Source, current.Mode);

            // The replaced session is always linked; attempts count only the same code's tries, so
            // switching to another participant (or correcting a mistyped code) starts at 1.
            int attempt = identity.ParticipantId == current.ParticipantId
                ? (current.Attempt < 1 ? 1 : current.Attempt) + 1
                : 1;
            return identity.ChainedTo(current.SessionId ?? string.Empty, attempt);
        }

        /// <summary>
        /// An explicit start that replaces a start still waiting to be consumed. It keeps the
        /// pending start's link to the session it restarts; the same code keeps that attempt
        /// number, another code starts at 1.
        /// </summary>
        public static SessionStartRequest Supersede(SessionStartRequest pending, SessionStartRequest requested)
        {
            int attempt = requested.ParticipantId == pending.ParticipantId ? pending.Attempt : 1;
            return requested.ChainedTo(pending.PreviousSessionId, attempt);
        }
    }
}
