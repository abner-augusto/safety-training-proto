using System.Collections.Generic;

namespace SafetyProto.Core.Events
{
    /// <summary>Something that can hold the session paused. The names are the session log's
    /// <c>pauseSource</c> tokens, so renaming one reshapes logs already collected.</summary>
    public enum PauseSource
    {
        /// <summary>Default of an event built outside <c>SessionPause</c>; never held.</summary>
        Unspecified = 0,
        /// <summary>The Android window lost focus (<c>OnApplicationFocus(false)</c>).</summary>
        ApplicationFocus,
        /// <summary>The activity paused (<c>OnApplicationPause(true)</c>): the headset went to sleep.</summary>
        ApplicationPause,
        /// <summary>The OpenXR session left FOCUSED while frames keep running: system menu,
        /// guardian, or a headset just taken off and not yet asleep.</summary>
        XrSessionUnfocused,
        Popup,
        PauseMenu,
        /// <summary>The practice area before the training is open.</summary>
        Practice
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

        public bool IsHeldBy(PauseSource source) => _sources.Contains(source);

        /// <summary>Whether <paramref name="source"/> is the one and only hold.</summary>
        public bool IsHeldOnlyBy(PauseSource source) => _sources.Count == 1 && _sources.Contains(source);

        /// <summary>The sources holding the pause right now, in declaration order.</summary>
        public PauseSource[] ActiveSources()
        {
            var active = new List<PauseSource>(_sources.Count);
            foreach (PauseSource source in System.Enum.GetValues(typeof(PauseSource)))
                if (_sources.Contains(source)) active.Add(source);
            return active.ToArray();
        }

        /// <summary>Returns true when this hold moved the session from running to paused.</summary>
        public bool Acquire(PauseSource source) => _sources.Add(source) && _sources.Count == 1;

        /// <summary>Returns true when this release moved the session from paused to running.</summary>
        public bool Release(PauseSource source) => _sources.Remove(source) && _sources.Count == 0;

        public void Clear() => _sources.Clear();
    }
}
