using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>Answers where the participant should be recentered for the current phase.</summary>
    public interface IRecenterAnchorProvider
    {
        /// <summary>Current phase's center anchor, or null when none applies.</summary>
        Transform CurrentAnchor { get; }
    }
}
