using System.Collections.Generic;

namespace SafetyProto.Runtime.Analytics
{
    /// Pure C# sliding-window detector for repeated events.
    /// Used by SafetyAnalyzer but independent of Unity, so it can be unit tested.
    public class SafetyPatternDetector
    {
        private readonly Queue<float> _timestamps = new Queue<float>();
        private readonly float _windowSeconds;
        private readonly int _threshold;

        public SafetyPatternDetector(float windowSeconds, int threshold)
        {
            _windowSeconds = windowSeconds;
            _threshold = threshold;
        }

        /// <summary>
        /// Records a violation occurrence at the provided time.
        /// Returns true when the threshold is met within the configured window.
        /// </summary>
        public bool RecordViolation(float time)
        {
            Prune(time);
            _timestamps.Enqueue(time);
            return _timestamps.Count >= _threshold;
        }

        /// <summary>
        /// Removes timestamps older than the sliding window relative to the provided time.
        /// </summary>
        public void Prune(float now)
        {
            var cutoff = now - _windowSeconds;
            while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
            {
                _timestamps.Dequeue();
            }
        }

        public int CurrentCount => _timestamps.Count;
    }
}
