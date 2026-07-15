#nullable enable
using System;
using Newtonsoft.Json;
using SafetyProto.Core;

namespace SafetyProto.Domain.Scoring
{
    /// <summary>
    /// Instructor-editable scoring block of a scenario document. Every number in
    /// the economy derives from a task's <see cref="TaskSeverity"/> through this
    /// config, so an author tunes one block instead of three magic numbers per
    /// task. All fields have sensible defaults; an absent "scoring" object in the
    /// JSON yields the default economy.
    /// </summary>
    public sealed class ScoringConfig
    {
        [JsonProperty("criticalPoints")] public int CriticalPoints { get; set; } = 200;
        [JsonProperty("moderatePoints")] public int ModeratePoints { get; set; } = 150;
        [JsonProperty("minorPoints")]    public int MinorPoints    { get; set; } = 100;

        [JsonProperty("criticalPenalty")] public int CriticalPenalty { get; set; } = 100;
        [JsonProperty("moderatePenalty")] public int ModeratePenalty { get; set; } = 50;
        [JsonProperty("minorPenalty")]    public int MinorPenalty    { get; set; } = 30;

        /// <summary>Fraction of the task's points still earned when it completes
        /// without required PPE (CompletedSuccessButUnsafe). Critical earns nothing.</summary>
        [JsonProperty("criticalUnsafeFactor")] public double CriticalUnsafeFactor { get; set; } = 0.0;
        [JsonProperty("moderateUnsafeFactor")] public double ModerateUnsafeFactor { get; set; } = 0.5;
        [JsonProperty("minorUnsafeFactor")]    public double MinorUnsafeFactor    { get; set; } = 0.7;

        /// <summary>Scales the per-pending-task charge at a failed inspection-gate
        /// press (each task is charged at most once per session).</summary>
        [JsonProperty("gateReductionFactor")] public double GateReductionFactor { get; set; } = 0.5;

        public static ScoringConfig Default { get; } = new ScoringConfig();

        public int PointsFor(TaskSeverity severity) => severity switch
        {
            TaskSeverity.Critical => CriticalPoints,
            TaskSeverity.Moderate => ModeratePoints,
            _ => MinorPoints
        };

        public int BasePenaltyFor(TaskSeverity severity) => severity switch
        {
            TaskSeverity.Critical => CriticalPenalty,
            TaskSeverity.Moderate => ModeratePenalty,
            _ => MinorPenalty
        };

        public double UnsafeFactorFor(TaskSeverity severity) => severity switch
        {
            TaskSeverity.Critical => CriticalUnsafeFactor,
            TaskSeverity.Moderate => ModerateUnsafeFactor,
            _ => MinorUnsafeFactor
        };

        /// <summary>Points actually earned by an unsafe completion of a task of
        /// this severity (rounded to nearest int).</summary>
        public int UnsafeEarnFor(TaskSeverity severity) =>
            (int)Math.Round(PointsFor(severity) * UnsafeFactorFor(severity));

        /// <summary>Charge for one pending task caught at a failed gate press.</summary>
        public int GateChargeFor(TaskSeverity severity) =>
            (int)Math.Round(BasePenaltyFor(severity) * GateReductionFactor);
    }
}
