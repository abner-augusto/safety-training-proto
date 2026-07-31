#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SafetyProto.Core;

namespace SafetyProto.Domain.Scoring
{
    /// <summary>Scoring for one risk tier. One object per level instead of a flat property
    /// per (level × parameter), so adding a tier does not add three more properties.</summary>
    public sealed class RiskLevelScoring
    {
        [JsonProperty("points")] public int Points { get; set; }

        /// <summary>Base penalty. The gate charge is this scaled by
        /// <see cref="ScoringConfig.GateReductionFactor"/>.</summary>
        [JsonProperty("penalty")] public int Penalty { get; set; }

        /// <summary>Fraction of <see cref="Points"/> still earned when the task completes
        /// without its required PPE (CompletedSuccessButUnsafe).</summary>
        [JsonProperty("unsafeFactor")] public double UnsafeFactor { get; set; }

        public RiskLevelScoring() { }

        public RiskLevelScoring(int points, int penalty, double unsafeFactor)
        {
            Points = points;
            Penalty = penalty;
            UnsafeFactor = unsafeFactor;
        }
    }

    /// <summary>
    /// Instructor-editable scoring block of a scenario document. Every number in the economy
    /// derives from a task's <see cref="RiskLevel"/> through this config, so an author tunes
    /// one block instead of three magic numbers per task.
    /// </summary>
    /// <remarks>
    /// Keyed by risk level (<c>"levels": { "substantial": { … } }</c>). The retired flat
    /// three-tier keys (<c>criticalPoints</c>, <c>minorPenalty</c>, …) are still read when no
    /// <c>levels</c> block is present, so scenarios authored before the risk matrix keep
    /// loading unchanged; they map minor→tolerable, moderate→moderate, critical→substantial and
    /// leave trivial/intolerable at their defaults.
    ///
    /// Defaults keep the three pre-existing tiers at exactly the values the scenario used
    /// before (100/150/200 points), so migrating a scenario changes a task's score only where
    /// the risk matrix actually reclassified it.
    /// </remarks>
    public sealed class ScoringConfig
    {
        private static readonly IReadOnlyDictionary<RiskLevel, RiskLevelScoring> Defaults =
            new Dictionary<RiskLevel, RiskLevelScoring>
            {
                [RiskLevel.Trivial]     = new RiskLevelScoring(50,  20,  0.8),
                [RiskLevel.Tolerable]   = new RiskLevelScoring(100, 30,  0.7),
                [RiskLevel.Moderate]    = new RiskLevelScoring(150, 50,  0.5),
                [RiskLevel.Substantial] = new RiskLevelScoring(200, 100, 0.0),
                [RiskLevel.Intolerable] = new RiskLevelScoring(250, 150, 0.0),
            };

        /// <summary>Per-level economy, keyed by the level token ("trivial" … "intolerable").
        /// Missing levels fall back to the defaults above.</summary>
        [JsonProperty("levels")]
        public Dictionary<string, RiskLevelScoring> Levels { get; set; } =
            new Dictionary<string, RiskLevelScoring>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Scales the per-pending-task charge at a failed inspection-gate press
        /// (each task is charged at most once per session).</summary>
        [JsonProperty("gateReductionFactor")] public double GateReductionFactor { get; set; } = 0.5;

        // ── Retired flat keys, read only when "levels" is absent ──────────────────

        [JsonProperty("criticalPoints")] public int? LegacyCriticalPoints { get; set; }
        [JsonProperty("moderatePoints")] public int? LegacyModeratePoints { get; set; }
        [JsonProperty("minorPoints")]    public int? LegacyMinorPoints { get; set; }

        [JsonProperty("criticalPenalty")] public int? LegacyCriticalPenalty { get; set; }
        [JsonProperty("moderatePenalty")] public int? LegacyModeratePenalty { get; set; }
        [JsonProperty("minorPenalty")]    public int? LegacyMinorPenalty { get; set; }

        [JsonProperty("criticalUnsafeFactor")] public double? LegacyCriticalUnsafeFactor { get; set; }
        [JsonProperty("moderateUnsafeFactor")] public double? LegacyModerateUnsafeFactor { get; set; }
        [JsonProperty("minorUnsafeFactor")]    public double? LegacyMinorUnsafeFactor { get; set; }

        public static ScoringConfig Default { get; } = new ScoringConfig();

        /// <summary>
        /// Folds any retired flat keys into <see cref="Levels"/>. Called once by
        /// <c>ScenarioLoader</c> after deserialization; safe to call twice.
        /// </summary>
        public void NormalizeLegacyKeys()
        {
            if (Levels.Count > 0) return;

            Apply(RiskLevel.Tolerable,   LegacyMinorPoints,    LegacyMinorPenalty,    LegacyMinorUnsafeFactor);
            Apply(RiskLevel.Moderate,    LegacyModeratePoints, LegacyModeratePenalty, LegacyModerateUnsafeFactor);
            Apply(RiskLevel.Substantial, LegacyCriticalPoints, LegacyCriticalPenalty, LegacyCriticalUnsafeFactor);

            void Apply(RiskLevel level, int? points, int? penalty, double? unsafeFactor)
            {
                if (points == null && penalty == null && unsafeFactor == null) return;

                var fallback = Defaults[level];
                Levels[RiskLevels.ToToken(level)] = new RiskLevelScoring(
                    points ?? fallback.Points,
                    penalty ?? fallback.Penalty,
                    unsafeFactor ?? fallback.UnsafeFactor);
            }
        }

        private RiskLevelScoring For(RiskLevel level) =>
            Levels.TryGetValue(RiskLevels.ToToken(level), out var authored) && authored != null
                ? authored
                : Defaults[level];

        public int PointsFor(RiskLevel level) => For(level).Points;

        public int BasePenaltyFor(RiskLevel level) => For(level).Penalty;

        public double UnsafeFactorFor(RiskLevel level) => For(level).UnsafeFactor;

        /// <summary>Points actually earned by an unsafe completion of a task at this level
        /// (rounded to nearest int).</summary>
        public int UnsafeEarnFor(RiskLevel level) =>
            (int)Math.Round(PointsFor(level) * UnsafeFactorFor(level));

        /// <summary>Charge for one pending task caught at a failed gate press.</summary>
        public int GateChargeFor(RiskLevel level) =>
            (int)Math.Round(BasePenaltyFor(level) * GateReductionFactor);
    }
}
