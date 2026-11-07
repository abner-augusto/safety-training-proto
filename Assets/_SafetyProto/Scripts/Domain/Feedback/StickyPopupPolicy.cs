namespace SafetyProto.Domain.Feedback
{
    /// <summary>
    /// A feedback popup that stays until the participant acts. For its reading time it holds the
    /// session clock, exactly as the self-closing popup it replaces did, so task times stay
    /// comparable; after that it stays visible without holding the clock until the participant's
    /// next action, or a ceiling. An action in the first instants is the one that opened the popup,
    /// so it does not count.
    /// </summary>
    public sealed class StickyPopupPolicy
    {
        /// <summary>Events this soon after the popup opened belong to the action that opened it.</summary>
        public const float CausalWindowSeconds = 0.5f;

        private readonly float _readSeconds;
        private readonly float _maxSeconds;
        private float _shownAt;
        private bool _acted;

        /// <param name="readSeconds">Reading time: the clock stays held and the popup cannot close.</param>
        /// <param name="maxSeconds">The popup closes at this age even without an action.</param>
        public StickyPopupPolicy(float shownAt, float readSeconds, float maxSeconds)
        {
            _shownAt = shownAt;
            _readSeconds = readSeconds > 0f ? readSeconds : 0f;
            _maxSeconds = maxSeconds > _readSeconds ? maxSeconds : _readSeconds;
        }

        /// <summary>The participant did something relevant: grabbed, attempted, equipped.</summary>
        public void NotifyAction(float now)
        {
            if (now - _shownAt >= CausalWindowSeconds) _acted = true;
        }

        /// <summary>Whether the popup still holds the session clock.</summary>
        public bool HoldsClock(float now) => now - _shownAt < _readSeconds;

        public bool ShouldClose(float now)
        {
            float age = now - _shownAt;
            return age >= _maxSeconds || (_acted && age >= _readSeconds);
        }
    }
}
