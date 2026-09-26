using System.Collections.Generic;
using UnityEngine;

namespace SafetyProto.Core.Events
{
    /// <summary>Something that can hold the session paused.</summary>
    public enum PauseSource
    {
        ApplicationFocus,
        Popup,
        PauseMenu
    }

    /// <summary>
    /// Tracks which sources currently hold the session paused. The session is paused while any
    /// source holds it; <see cref="Acquire"/> and <see cref="Release"/> report only the
    /// transitions, so one source resuming never cancels a pause another source still needs.
    /// </summary>
    public sealed class PauseHolds
    {
        private readonly HashSet<PauseSource> _sources = new HashSet<PauseSource>();

        public bool IsPaused => _sources.Count > 0;

        /// <summary>Returns true when this hold moved the session from running to paused.</summary>
        public bool Acquire(PauseSource source) => _sources.Add(source) && _sources.Count == 1;

        /// <summary>Returns true when this release moved the session from paused to running.</summary>
        public bool Release(PauseSource source) => _sources.Remove(source) && _sources.Count == 0;

        public void Clear() => _sources.Clear();
    }

    /// <summary>
    /// The single entry point for pausing the session. Pause sources call <see cref="Hold"/> and
    /// <see cref="Release"/> instead of raising SessionPaused/SessionResumed themselves, so
    /// every listener sees one balanced pair per pause no matter how many sources overlap.
    /// </summary>
    public static class SessionPause
    {
        private static readonly PauseHolds Holds = new PauseHolds();

        /// <summary>Current pause state. Unlike the events, which dispatch on the next
        /// EventBus pump, this reflects a hold immediately.</summary>
        public static bool IsPaused => Holds.IsPaused;

        public static void Hold(PauseSource source)
        {
            if (Holds.Acquire(source))
                SessionEvents.RaiseSessionPaused();
        }

        public static void Release(PauseSource source)
        {
            if (Holds.Release(source))
                SessionEvents.RaiseSessionResumed();
        }

        /// <summary>Drops every hold without raising SessionResumed. For session teardown, where
        /// a source destroyed mid-pause would otherwise leave the next session paused.</summary>
        public static void Reset() => Holds.Clear();

        // Domain reload is disabled on entering Play Mode, so static state survives between runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Holds.Clear();
    }
}
