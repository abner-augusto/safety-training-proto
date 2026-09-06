#nullable enable

namespace SafetyProto.Domain.Feedback
{
    /// <summary>What a feedback sound is telling the participant, ordered by how much it
    /// matters. Higher wins when two land together.</summary>
    public enum AudioFeedbackKind
    {
        Success = 0,
        UnsafeSuccess = 1,
        Failure = 2,
        Critical = 3,
    }

    /// <summary>
    /// Picks the one sound that plays when several feedback events land at once.
    ///
    /// A single action routinely raises more than one: completing a task without the required
    /// PPE publishes both the violation and the unsafe completion, so the failure and the
    /// unsafe-success clips used to be mixed on top of each other. Two rules solve that:
    /// requests arriving in the same frame collapse to the most important one, and a clip
    /// already sounding is only interrupted by something strictly more important than itself —
    /// anything else is dropped rather than layered.
    ///
    /// Pure and time-injected so the policy is testable without an AudioSource.
    /// </summary>
    public sealed class AudioFeedbackArbiter
    {
        private bool _hasPending;
        private AudioFeedbackKind _pending;

        private AudioFeedbackKind _playing;
        private float _playingUntil = float.NegativeInfinity;

        /// <summary>Asks for <paramref name="kind"/> to be played. Keeps only the most important
        /// request of the frame.</summary>
        public void Request(AudioFeedbackKind kind)
        {
            if (!_hasPending || kind > _pending)
            {
                _pending = kind;
                _hasPending = true;
            }
        }

        /// <summary>
        /// Resolves the frame's requests. Returns true (with the winning kind) when the caller
        /// should play it; false when there was nothing to play or the request lost to a clip
        /// that is still sounding. Either way the pending request is consumed — feedback is
        /// about what just happened, so a dropped request is not worth replaying later.
        /// </summary>
        public bool TryResolve(float now, out AudioFeedbackKind winner)
        {
            winner = default;
            if (!_hasPending) return false;

            var candidate = _pending;
            _hasPending = false;

            bool stillSounding = now < _playingUntil;
            if (stillSounding && candidate <= _playing) return false;

            winner = candidate;
            return true;
        }

        /// <summary>Records what the caller actually started playing and for how long.</summary>
        public void NotifyPlaying(AudioFeedbackKind kind, float now, float clipLength)
        {
            _playing = kind;
            _playingUntil = now + (clipLength > 0f ? clipLength : 0f);
        }

        public void Reset()
        {
            _hasPending = false;
            _playingUntil = float.NegativeInfinity;
        }
    }
}
