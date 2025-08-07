using System;

/// <summary>
/// Contract for any class that manages player score.
/// Keeps Unity‑specific concerns out so it can be unit‑tested in plain .NET.
/// </summary>
public interface IScoreService
{
    /// <summary>Current running score.</summary>
    int CurrentScore { get; }

    /// <summary>Adds points; <paramref name="amount"/> must be positive.</summary>
    void AddPoints(int amount, string reason);

    /// <summary>Subtracts points; <paramref name="amount"/> must be positive.</summary>
    void SubtractPoints(int amount, string reason);

    /// <summary>Raised every time the score changes.</summary>
    event Action<int /*newScore*/, int /*delta*/, string /*reason*/> ScoreChanged;
}