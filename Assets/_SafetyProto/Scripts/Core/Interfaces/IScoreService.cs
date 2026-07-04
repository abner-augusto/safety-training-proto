using System;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Contract for any class that manages player score.
    /// Keeps Unity-specific concerns out so it can be unit-tested in plain .NET.
    /// </summary>
    public interface IScoreService
    {
        /// <summary>Current running score.</summary>
        int CurrentScore { get; }

        /// <summary>Adds points; <paramref name="amount"/> must be positive. <paramref name="taskId"/>
        /// is the stable id of the task that caused the change (empty when not task-driven).</summary>
        void AddPoints(int amount, string reason, string taskId = "");

        /// <summary>Subtracts points; <paramref name="amount"/> must be positive. <paramref name="taskId"/>
        /// is the stable id of the task that caused the change (empty when not task-driven).</summary>
        void SubtractPoints(int amount, string reason, string taskId = "");

        /// <summary>Raised every time the score changes.</summary>
        event Action<int /*newScore*/, int /*delta*/, string /*reason*/, string /*taskId*/> ScoreChanged;
    }
}
