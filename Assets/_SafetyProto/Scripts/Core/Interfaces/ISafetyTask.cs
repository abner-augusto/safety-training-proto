using System.Collections.Generic;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// One classification choice offered by a hazard-classification task's report popup
    /// (e.g. "report_damaged_safety_mesh"). Stable per-option id, not an array index, is
    /// the analysis key — a reorder in the scenario must not reshape collected data.
    /// </summary>
    public interface IReportOption
    {
        string Id { get; }
        string Label { get; }
        bool Correct { get; }
    }

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

        /// <summary>Finish-screen advice for a task the participant did NOT perform —
        /// left never attempted at the phase gate, or closed as failed at the inspection
        /// gate: what skipping it means in the real world, with the NR citation. Falls
        /// back to hintText when empty.</summary>
        string omissionAdvice { get; }

        /// <summary>
        /// Returns the canonical action id this task expects. Mirrors
        /// <c>SafetyTask.ResolveExpectedActionId()</c>.
        /// </summary>
        string ResolveExpectedActionId();

        /// <summary>
        /// Classification choices for a hazard-classification task, authored in the
        /// scenario. Empty for every other task, which keeps the plain confirm/cancel
        /// report flow unchanged.
        /// </summary>
        IReadOnlyList<IReportOption> reportOptions { get; }

        /// <summary>Report-popup copy, authored alongside <see cref="reportOptions"/>.
        /// Empty when the task has none; a reporter falls back to its own defaults.</summary>
        string reportPopupTitle { get; }
        string reportPopupBody { get; }
        string reportConfirmLabel { get; }
        string reportCancelLabel { get; }
    }
}
