#nullable enable
using System;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Scoring
{
    public sealed class ScoreService : IScoreService, ISessionResettable
    {
        private static ScoreService? _instance;

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

        /// <summary>Reason published by <see cref="ResetSession"/>; consumers of
        /// <see cref="ScoreChanged"/> must not read its delta as a penalty.</summary>
        public const string ResetReason = "Point reset";

        public int CurrentScore { get; private set; }

        public event Action<int, int, string, string> ScoreChanged = delegate { };

        /// <summary>Penalties taken off this service's score since the last reset.</summary>
        public ScoreLedger Ledger { get; }

        public ScoreService()
        {
            Ledger = new ScoreLedger(this);
        }

        public void AddPoints(int amount, string reason, string taskId)
        {
            if (amount <= 0) throw new ArgumentException("Amount must be positive", nameof(amount));
            ChangeScore(+amount, reason, taskId);
        }

        public void SubtractPoints(int amount, string reason, string taskId)
        {
            if (amount <= 0) throw new ArgumentException("Amount must be positive", nameof(amount));
            ChangeScore(-amount, reason, taskId);
        }

        private void ChangeScore(int delta, string reason, string taskId)
        {
            CurrentScore += delta;
            ScoreChanged.Invoke(CurrentScore, delta, reason, taskId ?? string.Empty);
        }

        public void ResetSession()
        {
            var old = CurrentScore;
            CurrentScore = 0;
            ScoreChanged.Invoke(0, -old, ResetReason, string.Empty);
        }
    }
}
