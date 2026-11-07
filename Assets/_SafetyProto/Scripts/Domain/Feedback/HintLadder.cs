namespace SafetyProto.Domain.Feedback
{
    /// <summary>
    /// Guided-mode help in steps, by time without progress on the current task: the next object
    /// pulses, then a voice line says the step, then the evaluator is alerted. Each level is
    /// reached once, in order; any progress starts the count again from zero and drops the level.
    /// The owner feeds only the time that counts (a running, unpaused session with a task), so the
    /// thresholds read as seconds of the participant's own time on the task.
    /// </summary>
    public sealed class HintLadder
    {
        private readonly float[] _thresholds;

        /// <param name="thresholds">Seconds without progress at which levels 1, 2, 3… start; ascending.</param>
        public HintLadder(params float[] thresholds)
        {
            _thresholds = thresholds ?? System.Array.Empty<float>();
        }

        /// <summary>The highest level reached since the last progress; 0 when none.</summary>
        public int Level { get; private set; }

        public float SecondsWithoutProgress { get; private set; }

        /// <summary>The participant moved the task forward: back to no help.</summary>
        public void Progress()
        {
            Level = 0;
            SecondsWithoutProgress = 0f;
        }

        /// <summary>Adds <paramref name="seconds"/> without progress; returns the level reached now, or 0.</summary>
        public int Advance(float seconds)
        {
            if (seconds > 0f) SecondsWithoutProgress += seconds;
            if (Level >= _thresholds.Length || SecondsWithoutProgress < _thresholds[Level]) return 0;
            Level++;
            return Level;
        }
    }
}
