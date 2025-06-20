using System;

/// <summary>
/// Pure C# implementation of <see cref="IScoreService"/>.
/// No UnityEngine references so it runs in headless tests.
/// </summary>
public sealed class ScoreService : IScoreService
{
    public int CurrentScore { get; private set; }

    public event Action<int, int, string> ScoreChanged = delegate { };

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
}