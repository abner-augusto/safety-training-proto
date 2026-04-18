using System;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Core
{
    public sealed class ScoreService : IScoreService, ISessionResettable
    {
        private static ScoreService _instance;

        public static ScoreService Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new ScoreService();
                return _instance;
            }
        }

        public static void DestroyInstance()
        {
            _instance = null!;
        }

        public int CurrentScore { get; private set; }

        public event Action<int, int, string> ScoreChanged = delegate { };

        public ScoreService() { }

        public void AddPoints(int amount, string reason)
        {
            if (amount <= 0) throw new ArgumentException("Amount must be positive", nameof(amount));
            ChangeScore(+amount, reason);
        }

        public void SubtractPoints(int amount, string reason)
        {
            if (amount <= 0) throw new ArgumentException("Amount must be positive", nameof(amount));
            ChangeScore(-amount, reason);
        }

        private void ChangeScore(int delta, string reason)
        {
            CurrentScore += delta;
            ScoreChanged.Invoke(CurrentScore, delta, reason);
        }

        public void ResetSession()
        {
            var old = CurrentScore;
            CurrentScore = 0;
            ScoreChanged.Invoke(0, -old, "Point reset");
        }
    }
}
