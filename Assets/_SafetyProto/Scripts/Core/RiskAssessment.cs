#nullable enable
using System;

namespace SafetyProto.Core
{
    /// <summary>
    /// A task's occupational risk as NR-01 asks it to be recorded: the two graded axes the
    /// safety specialist actually judges, plus the level they combine into.
    /// </summary>
    /// <remarks>
    /// Storing the inputs and deriving the level — instead of storing the level alone — is what
    /// NR-01 item 1.5.4.4.2.2 is after: the criteria and the grades behind a classification have
    /// to be documented, not just the verdict. It also means re-tuning the bands reclassifies
    /// every task at once, with no re-authoring.
    ///
    /// <see cref="Severity"/> is NR-01 1.5.4.4.4 (magnitude of the worst possible consequence,
    /// per 1.5.4.4.4.1). <see cref="Probability"/> is 1.5.4.4.5 — for accident-borne injuries,
    /// 1.5.4.4.5.4 grades it by exposure to the hazard and the effectiveness of the prevention
    /// measures in place. It is deliberately NOT the chance that a worker skips the item; that
    /// is an adherence figure, it drives pedagogical priority rather than the risk level, and
    /// mixing the two into one number loses both.
    ///
    /// Legacy scenarios carry only a level token and no grades; <see cref="FromLevel"/> builds
    /// those, and <see cref="HasGrades"/> is false for them.
    /// </remarks>
    [Serializable]
    public readonly struct RiskAssessment : IEquatable<RiskAssessment>
    {
        public const int MinGrade = 1;
        public const int MaxGrade = 5;

        /// <summary>Magnitude of the worst possible consequence, 1–5. 0 when not authored.</summary>
        public int Severity { get; }

        /// <summary>Chance of the injury occurring, 1–5. 0 when not authored.</summary>
        public int Probability { get; }

        public RiskLevel Level { get; }

        private RiskAssessment(int severity, int probability, RiskLevel level)
        {
            Severity = severity;
            Probability = probability;
            Level = level;
        }

        /// <summary>True when both axes were authored, so <see cref="Index"/> is meaningful
        /// and <see cref="Level"/> was derived rather than declared.</summary>
        public bool HasGrades => Severity > 0 && Probability > 0;

        /// <summary>Risk index S × P, 1–25. 0 when the grades were not authored.</summary>
        public int Index => HasGrades ? Severity * Probability : 0;

        /// <summary>Default for anything unauthored — the middle tier, matching the previous
        /// <c>TaskSeverity.Moderate</c> fallback.</summary>
        public static RiskAssessment Default { get; } = FromLevel(RiskLevel.Moderate);

        /// <summary>Builds an assessment from the two graded axes, deriving the level.
        /// Grades outside 1–5 are clamped rather than rejected: a bad number in one task
        /// should not stop a whole scenario from loading.</summary>
        public static RiskAssessment FromGrades(int severity, int probability)
        {
            var s = Clamp(severity);
            var p = Clamp(probability);
            return new RiskAssessment(s, p, LevelForIndex(s * p));
        }

        /// <summary>Builds an assessment from a declared level, with no grades behind it.</summary>
        public static RiskAssessment FromLevel(RiskLevel level) => new RiskAssessment(0, 0, level);

        /// <summary>
        /// Bands over the 1–25 index. Linear rescale of the risk-index bands published in the
        /// labour inspectorate's GRO/PGR material (1–100: 1–18 trivial, 19–39 tolerable, 40–62
        /// moderate, 63–90 substantial, 91–100 intolerable). Intolerable therefore needs both
        /// axes at their maximum — nothing interposed between the worker and a fatal outcome.
        /// </summary>
        public static RiskLevel LevelForIndex(int index)
        {
            if (index <= 4) return RiskLevel.Trivial;
            if (index <= 9) return RiskLevel.Tolerable;
            if (index <= 15) return RiskLevel.Moderate;
            if (index <= 22) return RiskLevel.Substantial;
            return RiskLevel.Intolerable;
        }

        private static int Clamp(int grade) =>
            grade < MinGrade ? MinGrade : grade > MaxGrade ? MaxGrade : grade;

        public bool Equals(RiskAssessment other) =>
            Severity == other.Severity && Probability == other.Probability && Level == other.Level;

        public override bool Equals(object? obj) => obj is RiskAssessment other && Equals(other);

        public override int GetHashCode() => (Severity * 397 ^ Probability) * 397 ^ (int)Level;

        public override string ToString() => HasGrades
            ? $"{RiskLevels.DisplayName(Level)} (S{Severity}×P{Probability}={Index})"
            : RiskLevels.DisplayName(Level);
    }
}
