#nullable enable
using System;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Scoring
{
    /// <summary>One penalty taken off the score. <see cref="Amount"/> is positive.</summary>
    public readonly struct ScoreCharge
    {
        public ScoreCharge(string code, int amount, string taskId)
        {
            Code = code;
            Amount = amount;
            TaskId = taskId;
        }

        /// <summary>The reason the score service was given, a <see cref="ViolationCodes"/> value.</summary>
        public string Code { get; }

        public int Amount { get; }

        /// <summary>Empty when the charge belongs to no task.</summary>
        public string TaskId { get; }
    }

    /// <summary>
    /// Keeps the penalties a session took off the score. The score service only publishes
    /// <c>ScoreChanged</c>, so without this the finish report could show earned points but
    /// never explain why the total is lower.
    /// </summary>
    public sealed class ScoreLedger : IDisposable
    {
        private readonly IScoreService _scoreService;
        private readonly List<ScoreCharge> _charges = new();

        public ScoreLedger(IScoreService scoreService)
        {
            _scoreService = scoreService;
            _scoreService.ScoreChanged += OnScoreChanged;
        }

        public IReadOnlyList<ScoreCharge> Charges => _charges;

        public int TotalCharged
        {
            get
            {
                int total = 0;
                foreach (var charge in _charges) total += charge.Amount;
                return total;
            }
        }

        /// <summary>Charges recorded against <paramref name="taskId"/>, in the order they happened.</summary>
        public IReadOnlyList<ScoreCharge> ChargesFor(string taskId)
        {
            var result = new List<ScoreCharge>();
            if (string.IsNullOrEmpty(taskId)) return result;
            foreach (var charge in _charges)
                if (string.Equals(charge.TaskId, taskId, StringComparison.Ordinal)) result.Add(charge);
            return result;
        }

        /// <summary>
        /// Charges that no row of the report claims: no task id, or an id outside
        /// <paramref name="reportedTaskIds"/>. Together with the per-task charges they account for
        /// every point taken off.
        /// </summary>
        public IReadOnlyList<ScoreCharge> ChargesOutside(ICollection<string> reportedTaskIds)
        {
            var result = new List<ScoreCharge>();
            foreach (var charge in _charges)
                if (string.IsNullOrEmpty(charge.TaskId) || !reportedTaskIds.Contains(charge.TaskId))
                    result.Add(charge);
            return result;
        }

        public void Dispose() => _scoreService.ScoreChanged -= OnScoreChanged;

        private void OnScoreChanged(int newScore, int delta, string reason, string taskId)
        {
            // The reset publishes a negative delta that undoes the old total; it is not a charge.
            if (reason == ScoreService.ResetReason)
            {
                _charges.Clear();
                return;
            }

            if (delta < 0)
                _charges.Add(new ScoreCharge(reason, -delta, taskId ?? string.Empty));
        }
    }
}
