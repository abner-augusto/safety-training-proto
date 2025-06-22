using UnityEngine;

/// <summary>
/// ScriptableObject wrapper so the pure <see cref="ScoreService"/> can be shared
/// via the Unity Inspector. Drag this asset into scene objects that need scoring.
/// </summary>
[CreateAssetMenu(fileName = "ScoreService", menuName = "Scoring/Service Asset")]
public class ScoreServiceSO : ScriptableObject
{
    // Backing field is kept private so every referencing object shares the same instance.
    private readonly ScoreService _service = new ScoreService();

    /// <summary>Exposes the underlying service as an interface.</summary>
    public IScoreService Service => _service;
}