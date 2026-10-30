using UnityEngine;

namespace SafetyProto.Core.Events
{
    /// <summary>
    /// The single entry point for pausing the session. Pause sources call <see cref="Hold"/> and
    /// <see cref="Release"/> instead of raising SessionPaused/SessionResumed themselves, so
    /// every listener sees one balanced pair per pause no matter how many sources overlap.
    /// Each change of the hold set is also published as <see cref="PauseHoldChangedEventArgs"/>,
    /// ahead of the transition event it causes, so overlapping pauses can be reconstructed.
    /// </summary>
    public static class SessionPause
    {
        private static readonly PauseHolds Holds = new PauseHolds();

        /// <summary>Current pause state. Unlike the events, which dispatch on the next
        /// EventBus pump, this reflects a hold immediately.</summary>
        public static bool IsPaused => Holds.IsPaused;

        /// <summary>The sources holding the pause right now, for a late observer (a dashboard
        /// that connects mid-pause) that missed the hold events.</summary>
        public static PauseSource[] ActiveSources() => Holds.ActiveSources();

        public static void Hold(PauseSource source)
        {
            if (Holds.IsHeldBy(source)) return;
            bool paused = Holds.Acquire(source);
            var active = Holds.ActiveSources();
            SessionEvents.RaisePauseHoldChanged(new PauseHoldChangedEventArgs { Source = source, Held = true, ActiveSources = active });
            if (paused)
                SessionEvents.RaiseSessionPaused(new SessionPausedEventArgs { Source = source, ActiveSources = active });
        }

        public static void Release(PauseSource source)
        {
            if (!Holds.IsHeldBy(source)) return;
            bool resumed = Holds.Release(source);
            SessionEvents.RaisePauseHoldChanged(new PauseHoldChangedEventArgs { Source = source, Held = false, ActiveSources = Holds.ActiveSources() });
            if (resumed)
                SessionEvents.RaiseSessionResumed(new SessionResumedEventArgs { Source = source });
        }

        /// <summary>Drops every hold without raising SessionResumed. For session teardown, where
        /// a source destroyed mid-pause would otherwise leave the next session paused.</summary>
        public static void Reset() => Holds.Clear();

        // Domain reload is disabled on Play Mode entry; static state persists between runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Holds.Clear();
    }
}
