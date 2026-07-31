using System.Collections.Generic;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Engine-independent view of a safety training task.
    /// Implemented by the Unity <c>SafetyTask</c> ScriptableObject and by pure-C#
    /// records used in the CLI harness.
    /// </summary>
    public interface ISafetyTask
    {
        /// <summary>
        /// Stable, language-independent identifier for the task (e.g. "equip_helmet").
        /// Used as the analysis key in session logs; unlike <see cref="taskName"/> it is not
        /// localized and does not change when display copy is edited. Implementations that lack
        /// an authored id fall back to <see cref="taskName"/> so this is never empty.
        /// </summary>
        string id { get; }

        string taskName { get; }
        string taskDescription { get; }

        /// <summary>
        /// Occupational risk of the task: the severity and probability grades the safety
        /// specialist assessed, plus the level they derive (NR-01 GRO). Drives all
        /// point/penalty math via the scenario's ScoringConfig.
        /// </summary>
        RiskAssessment risk { get; }

        /// <summary>Shorthand for <c>risk.Level</c> — the tier every scoring rule keys on.</summary>
        RiskLevel riskLevel { get; }
        IReadOnlyList<PPEType> requiredPPE { get; }
        string hintText { get; }
        string failureAdvice { get; }
        string ppeAdvice { get; }

        /// <summary>Finish-screen advice for an OMITTED task: what skipping it means
        /// in the real world, with the NR citation. Falls back to hintText when empty.</summary>
        string omissionAdvice { get; }

        /// <summary>
        /// Returns the canonical action id this task expects. Mirrors
        /// <c>SafetyTask.ResolveExpectedActionId()</c>.
        /// </summary>
        string ResolveExpectedActionId();
    }
}
