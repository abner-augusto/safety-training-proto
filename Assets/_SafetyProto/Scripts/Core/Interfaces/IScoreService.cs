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
        /// is the stable id of the task that caused the change; pass <see cref="string.Empty"/> when
        /// the change is not task-driven. Required rather than optional: C# resolves a parameter
        /// default from the static type at the call site instead of inheriting it, so an optional
        /// parameter here lets an implementation declare a different default and silently disagree
        /// with this contract.</summary>
        void AddPoints(int amount, string reason, string taskId);

        /// <summary>Subtracts points; <paramref name="amount"/> must be positive. See
        /// <see cref="AddPoints"/> for <paramref name="taskId"/>.</summary>
        void SubtractPoints(int amount, string reason, string taskId);

        /// <summary>Raised every time the score changes.</summary>
        event Action<int /*newScore*/, int /*delta*/, string /*reason*/, string /*taskId*/> ScoreChanged;
    }
}
