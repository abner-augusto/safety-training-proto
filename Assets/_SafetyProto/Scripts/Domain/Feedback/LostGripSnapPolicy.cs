namespace SafetyProto.Domain.Feedback
{
    /// <summary>Why a held item counts as let go at the slot it was just at.</summary>
    public enum LostGripCause
    {
        None,

        /// <summary>The holding hand stopped being tracked.</summary>
        TrackingLost,

        /// <summary>The item left the slot faster than a hand moves: a tracking jump.</summary>
        Jump,
    }

    /// <summary>
    /// A participant equipping PPE by pressing it to the body (the helmet onto the head, the
    /// harness onto the chest) hides the hand from the headset cameras before it opens, so the
    /// grab never ends at the slot: the hand freezes or jumps away with the item. While the item
    /// is at a free slot, or was there a moment ago, losing the hand's tracking or a jump out of
    /// the slot counts as letting go there. Each hold fires at most once.
    /// </summary>
    public sealed class LostGripSnapPolicy
    {
        private readonly float _graceSeconds;
        private readonly float _lostConfirmSeconds;
        private readonly float _jumpSpeed;
        private float _lastNearAt = float.NegativeInfinity;
        private float _lostSince = -1f;
        private bool _fired;

        /// <param name="graceSeconds">How long after leaving the slot a lost grip still counts.</param>
        /// <param name="lostConfirmSeconds">How long tracking must stay lost, so a one-frame dropout does not fire.</param>
        /// <param name="jumpSpeed">Item speed (m/s) above which leaving the slot is a jump, not a hand moving.</param>
        public LostGripSnapPolicy(float graceSeconds, float lostConfirmSeconds, float jumpSpeed)
        {
            _graceSeconds = graceSeconds > 0f ? graceSeconds : 0f;
            _lostConfirmSeconds = lostConfirmSeconds > 0f ? lostConfirmSeconds : 0f;
            _jumpSpeed = jumpSpeed > 0f ? jumpSpeed : float.PositiveInfinity;
        }

        /// <summary>Starts a new hold.</summary>
        public void Reset()
        {
            _lastNearAt = float.NegativeInfinity;
            _lostSince = -1f;
            _fired = false;
        }

        /// <summary>Whether the item was at a free slot within the grace window.</summary>
        public bool RecentlyNear(float now) => now - _lastNearAt <= _graceSeconds;

        /// <summary>Observes one frame of a hold that has not snapped.</summary>
        /// <param name="nearSlot">A free slot that takes the item is in reach now.</param>
        /// <param name="handTracked">Every hand holding the item is tracked.</param>
        /// <param name="speed">The item's speed this frame, m/s.</param>
        public LostGripCause Observe(float now, bool nearSlot, bool handTracked, float speed)
        {
            if (nearSlot) _lastNearAt = now;

            if (handTracked) _lostSince = -1f;
            else if (_lostSince < 0f) _lostSince = now;

            if (_fired || !RecentlyNear(now)) return LostGripCause.None;

            if (!handTracked && now - _lostSince >= _lostConfirmSeconds)
                return Fire(LostGripCause.TrackingLost);

            if (!nearSlot && speed >= _jumpSpeed)
                return Fire(LostGripCause.Jump);

            return LostGripCause.None;
        }

        /// <summary>
        /// For a release that happened away from any slot: whether it still counts as letting go at
        /// the slot, because the hand was untracked when the grab ended, right after being there.
        /// </summary>
        public bool CountsAtSlotOnRelease(float now, bool handTracked) =>
            !_fired && !handTracked && RecentlyNear(now);

        private LostGripCause Fire(LostGripCause cause)
        {
            _fired = true;
            return cause;
        }
    }
}
