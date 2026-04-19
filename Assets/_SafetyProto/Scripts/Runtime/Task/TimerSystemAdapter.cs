using SafetyProto.Core.Interfaces;

namespace SafetyProto.Runtime.Task
{
    internal sealed class TimerSystemAdapter : ITimerSource
    {
        private readonly TimerSystem _timerSystem;

        public TimerSystemAdapter(TimerSystem timerSystem)
        {
            _timerSystem = timerSystem;
        }

        public float ElapsedSeconds => _timerSystem.GetTotalSessionTime();
    }
}
