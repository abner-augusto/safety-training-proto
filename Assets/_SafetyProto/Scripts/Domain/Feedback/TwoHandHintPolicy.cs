namespace SafetyProto.Domain.Feedback
{
    /// <summary>What the two-hands warning should do on this frame.</summary>
    public readonly struct TwoHandHintStep
    {
        public TwoHandHintStep(bool show, bool hide, bool playCue)
        {
            Show = show;
            Hide = hide;
            PlayCue = playCue;
        }

        /// <summary>Start showing where the second hand goes.</summary>
        public bool Show { get; }

        /// <summary>Stop showing it: the second hand arrived, or the piece was let go.</summary>
        public bool Hide { get; }

        /// <summary>Play the short warning sound.</summary>
        public bool PlayCue { get; }
    }

    /// <summary>
    /// When a piece that needs both hands is held with one: after a short grace period (a
    /// participant reaching with the second hand should not hear a warning), show where the other
    /// hand goes and sound a short cue. The cue has a cooldown, so regrabbing in a loop does not
    /// nag; the visual follows the hand count every frame.
    /// </summary>
    public sealed class TwoHandHintPolicy
    {
        private readonly float _delaySeconds;
        private readonly float _cueCooldownSeconds;
        private float _oneHandSince = -1f;
        private float _nextCueAt;

        /// <param name="delaySeconds">How long one hand must hold before the warning shows.</param>
        /// <param name="cueCooldownSeconds">Minimum time between two warning sounds.</param>
        public TwoHandHintPolicy(float delaySeconds, float cueCooldownSeconds)
        {
            _delaySeconds = delaySeconds > 0f ? delaySeconds : 0f;
            _cueCooldownSeconds = cueCooldownSeconds > 0f ? cueCooldownSeconds : 0f;
        }

        public bool Showing { get; private set; }

        public TwoHandHintStep Tick(float now, int handCount)
        {
            if (handCount != 1)
            {
                _oneHandSince = -1f;
                if (!Showing) return default;
                Showing = false;
                return new TwoHandHintStep(show: false, hide: true, playCue: false);
            }

            if (_oneHandSince < 0f) _oneHandSince = now;
            if (Showing || now - _oneHandSince < _delaySeconds) return default;

            Showing = true;
            bool cue = now >= _nextCueAt;
            if (cue) _nextCueAt = now + _cueCooldownSeconds;
            return new TwoHandHintStep(show: true, hide: false, playCue: cue);
        }
    }
}
