using System;

namespace SafetyProto.Runtime.Interaction
{
    public enum GazeDwellState
    {
        Idle,
        Dwelling,
        Completed
    }

    /// <summary>
    /// Accumulates continuous gaze into a 0..1 dwell progress.
    ///
    /// Engine-independent on purpose: no UnityEngine types, so the whole behaviour is unit-testable
    /// with an injected delta time and no scene, no physics and no headset.
    ///
    /// Three timings shape the feel:
    /// <list type="bullet">
    /// <item>dwellDuration — seconds of gaze needed to complete.</item>
    /// <item>graceSeconds — how long losing the target is forgiven before draining starts. Head
    /// tracking jitter and the collider edge make momentary losses routine; without this the ring
    /// visibly stutters and reads as broken.</item>
    /// <item>decaySeconds — how long a <em>full</em> ring takes to drain. The rate is deliberately
    /// independent of the current fill, so looking away always feels equally decisive, and is
    /// faster than filling so the reset reads as a reset.</item>
    /// </list>
    /// </summary>
    public class GazeDwellTimer
    {
        private readonly float _dwellDuration;
        private readonly float _graceSeconds;
        private readonly float _drainPerSecond;

        private float _elapsed;
        private float _graceRemaining;

        public GazeDwellTimer(float dwellDuration, float graceSeconds, float decaySeconds)
        {
            if (dwellDuration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(dwellDuration), "Dwell duration must be positive.");

            _dwellDuration = dwellDuration;
            _graceSeconds = graceSeconds < 0f ? 0f : graceSeconds;
            _drainPerSecond = dwellDuration / (decaySeconds <= 0f ? 0.01f : decaySeconds);
        }

        public GazeDwellState State { get; private set; } = GazeDwellState.Idle;

        /// <summary>Dwell completion in the 0..1 range, suitable for a radial fill.</summary>
        public float Progress => _elapsed / _dwellDuration;

        /// <summary>
        /// Advances the timer. Returns true on the single frame the dwell completes; completion
        /// latches, so later ticks return false and the progress stays at 1.
        /// </summary>
        public bool Tick(bool gazed, float deltaTime)
        {
            if (State == GazeDwellState.Completed) return false;

            if (gazed)
            {
                _graceRemaining = _graceSeconds;
                _elapsed += deltaTime;

                if (_elapsed >= _dwellDuration)
                {
                    _elapsed = _dwellDuration;
                    State = GazeDwellState.Completed;
                    return true;
                }

                State = GazeDwellState.Dwelling;
                return false;
            }

            if (_elapsed <= 0f)
            {
                State = GazeDwellState.Idle;
                return false;
            }

            if (_graceRemaining > 0f)
            {
                _graceRemaining -= deltaTime;
                return false;
            }

            _elapsed -= deltaTime * _drainPerSecond;
            if (_elapsed <= 0f)
            {
                _elapsed = 0f;
                State = GazeDwellState.Idle;
            }

            return false;
        }

        public void Reset()
        {
            _elapsed = 0f;
            _graceRemaining = 0f;
            State = GazeDwellState.Idle;
        }
    }
}
