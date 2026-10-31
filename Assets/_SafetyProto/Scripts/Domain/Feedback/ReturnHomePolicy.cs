namespace SafetyProto.Domain.Feedback
{
    /// <summary>What a returnable object looks like this frame, measured by its owner.</summary>
    public readonly struct ReturnHomeObservation
    {
        public ReturnHomeObservation(bool held, bool secured, float distanceFromHome, float dropBelowHome, bool outsideZone)
        {
            Held = held;
            Secured = secured;
            DistanceFromHome = distanceFromHome;
            DropBelowHome = dropBelowHome;
            OutsideZone = outsideZone;
        }

        /// <summary>At least one hand is holding the object.</summary>
        public bool Held { get; }

        /// <summary>The object rests where it belongs away from home: worn, installed.</summary>
        public bool Secured { get; }

        /// <summary>Metres between the object and its home position.</summary>
        public float DistanceFromHome { get; }

        /// <summary>Metres the object sits below its home height; negative when above it.</summary>
        public float DropBelowHome { get; }

        /// <summary>The object has left the optional area it is meant to stay in.</summary>
        public bool OutsideZone { get; }
    }

    /// <summary>Why a return is pending.</summary>
    public enum ReturnHomeTrigger
    {
        None,
        /// <summary>A hand let go of the object away from any slot or socket.</summary>
        Release,
        /// <summary>The object was pushed, knocked or dropped out of place without being held.</summary>
        Displaced,
        /// <summary>Its owner asked for a return (for example, a PPE item pulled off its slot).</summary>
        Requested,
    }

    /// <summary>
    /// Decides when a loose object travels back home. Every input is measured afresh each frame
    /// (held, secured, where the object is), never kept from grab events: an event that a disabled
    /// listener missed used to leave a PPE item marked as held forever, so it never came back after
    /// its first grab. A pending return survives frames without a tick, so an object switched off
    /// during the wait still returns once it is switched back on.
    /// </summary>
    public sealed class ReturnHomePolicy
    {
        private readonly float _delaySeconds;
        private readonly float _maxHomeDistance;
        private readonly float _maxDropBelowHome;
        private bool _wasHeld;

        /// <param name="delaySeconds">How long the object must stay released or displaced before it returns.</param>
        /// <param name="maxHomeDistance">Distance from home that counts as displaced; 0 turns the check off.</param>
        /// <param name="maxDropBelowHome">Drop below home height that counts as displaced (fell off the table
        /// or the platform); 0 turns the check off.</param>
        public ReturnHomePolicy(float delaySeconds, float maxHomeDistance, float maxDropBelowHome)
        {
            _delaySeconds = delaySeconds > 0f ? delaySeconds : 0f;
            _maxHomeDistance = maxHomeDistance > 0f ? maxHomeDistance : 0f;
            _maxDropBelowHome = maxDropBelowHome > 0f ? maxDropBelowHome : 0f;
        }

        /// <summary>Why a return is waiting for its delay, or <see cref="ReturnHomeTrigger.None"/>.</summary>
        public ReturnHomeTrigger Pending { get; private set; }

        /// <summary>Seconds the pending return has waited so far.</summary>
        public float PendingSeconds { get; private set; }

        /// <summary>Asks for a return after the usual delay, unless one is already pending.</summary>
        public void Request()
        {
            if (Pending != ReturnHomeTrigger.None) return;
            Pending = ReturnHomeTrigger.Requested;
            PendingSeconds = 0f;
        }

        /// <summary>Forgets any pending return and the last held state.</summary>
        public void Reset()
        {
            Pending = ReturnHomeTrigger.None;
            PendingSeconds = 0f;
            _wasHeld = false;
        }

        /// <summary>Whether this observation counts as out of place.</summary>
        public bool IsDisplaced(in ReturnHomeObservation observation)
        {
            if (observation.OutsideZone) return true;
            if (_maxHomeDistance > 0f && observation.DistanceFromHome > _maxHomeDistance) return true;
            return _maxDropBelowHome > 0f && observation.DropBelowHome > _maxDropBelowHome;
        }

        /// <summary>
        /// Advances the policy by <paramref name="deltaSeconds"/>. Returns true on the frame the object
        /// should start travelling home; <paramref name="fired"/> then says why.
        /// </summary>
        public bool Tick(float deltaSeconds, in ReturnHomeObservation observation, out ReturnHomeTrigger fired)
        {
            fired = ReturnHomeTrigger.None;

            if (observation.Held)
            {
                Pending = ReturnHomeTrigger.None;
                PendingSeconds = 0f;
                _wasHeld = true;
                return false;
            }

            bool released = _wasHeld;
            _wasHeld = false;

            if (observation.Secured)
            {
                Pending = ReturnHomeTrigger.None;
                PendingSeconds = 0f;
                return false;
            }

            if (released)
            {
                Pending = ReturnHomeTrigger.Release;
                PendingSeconds = 0f;
            }
            else
            {
                bool displaced = IsDisplaced(observation);
                if (Pending == ReturnHomeTrigger.None && displaced)
                {
                    Pending = ReturnHomeTrigger.Displaced;
                    PendingSeconds = 0f;
                }
                else if (Pending == ReturnHomeTrigger.Displaced && !displaced)
                {
                    // It rolled back into place on its own.
                    Pending = ReturnHomeTrigger.None;
                    PendingSeconds = 0f;
                }
                else if (Pending != ReturnHomeTrigger.None && deltaSeconds > 0f)
                {
                    PendingSeconds += deltaSeconds;
                }
            }

            if (Pending == ReturnHomeTrigger.None || PendingSeconds < _delaySeconds) return false;

            fired = Pending;
            Pending = ReturnHomeTrigger.None;
            PendingSeconds = 0f;
            return true;
        }
    }
}
