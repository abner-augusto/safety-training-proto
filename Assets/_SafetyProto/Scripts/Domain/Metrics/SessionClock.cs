#nullable enable
namespace SafetyProto.Domain.Metrics
{
    /// <summary>
    /// Session time as the participant experiences it: advances only while the session is
    /// running and not paused. Both the group countdown and the logged completion times read
    /// this clock, so a popup or a lost headset focus never inflates a recorded duration.
    /// </summary>
    public sealed class SessionClock
    {
        private bool _running;

        public float ElapsedSeconds { get; private set; }
        public bool IsRunning => _running;

        public void Start()
        {
            ElapsedSeconds = 0f;
            _running = true;
        }

        public void Tick(float deltaSeconds, bool paused)
        {
            if (!_running || paused || deltaSeconds <= 0f) return;
            ElapsedSeconds += deltaSeconds;
        }

        public void Stop() => _running = false;

        public void Reset()
        {
            _running = false;
            ElapsedSeconds = 0f;
        }
    }
}
