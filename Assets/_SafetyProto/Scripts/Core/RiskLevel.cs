namespace SafetyProto.Core
{
    /// <summary>
    /// Occupational risk level of a safety task, in the vocabulary of NR-01's GRO.
    /// </summary>
    /// <remarks>
    /// This is the <em>classification of a risk</em> (NR-01 item 1.5.4.4.3) — the result of
    /// combining severity with probability — and NOT the severity axis on its own. NR-01
    /// severity (1.5.4.4.4) is only the magnitude of the consequence and has no
    /// trivial/tolerable/…/intolerable classes. The retired <c>TaskSeverity</c> enum named
    /// this concept after the wrong axis; a safety specialist reading "Severidade:
    /// Intolerável" in the authoring tool would flag it as a normative error.
    ///
    /// Single driver of the scoring economy: points earned, unsafe-completion factor,
    /// timeout/gate/distractor penalties and the medal cap all derive from this tier through
    /// <c>ScoringConfig</c>.
    ///
    /// Ordinals are explicit and ordered so threshold rules compare directly
    /// (<c>level &gt;= RiskLevel.Substantial</c>), instead of enumerating equality cases —
    /// that is what let a new top tier silently fall out of the medal rule before.
    ///
    /// Identifiers are English per repo convention; <see cref="RiskLevels.DisplayName"/>
    /// carries the pt-BR term the specialist and the participant actually read.
    /// </remarks>
    public enum RiskLevel
    {
        Trivial = 1,
        Tolerable = 2,
        Moderate = 3,
        Substantial = 4,
        Intolerable = 5
    }

    public static class RiskLevels
    {
        /// <summary>Every level, ascending. Authoring UIs bind to this instead of
        /// hard-coding a list that can drift from the enum.</summary>
        public static readonly RiskLevel[] All =
        {
            RiskLevel.Trivial,
            RiskLevel.Tolerable,
            RiskLevel.Moderate,
            RiskLevel.Substantial,
            RiskLevel.Intolerable
        };

        /// <summary>
        /// At or above this level a task counts as an eliminatory fault: failing, omitting or
        /// completing it without PPE caps the medal. Maps the old <c>Critical</c> rule forward
        /// and, being a threshold, automatically covers <see cref="RiskLevel.Intolerable"/>.
        /// </summary>
        public const RiskLevel EliminatoryThreshold = RiskLevel.Substantial;

        /// <summary>
        /// Tier charged for violations that belong to no task (wrong PPE picked from the bench,
        /// equipping out of the recommended order). Tolerable, not Trivial, so the charge stays
        /// exactly what the retired <c>Minor</c> tier cost.
        /// </summary>
        public const RiskLevel IncidentalChargeTier = RiskLevel.Tolerable;

        /// <summary>pt-BR term from NR-01, for the authoring tool and any participant-facing surface.</summary>
        public static string DisplayName(RiskLevel level) => level switch
        {
            RiskLevel.Trivial => "Trivial",
            RiskLevel.Tolerable => "Tolerável",
            RiskLevel.Moderate => "Moderado",
            RiskLevel.Substantial => "Substancial",
            RiskLevel.Intolerable => "Intolerável",
            _ => level.ToString()
        };

        /// <summary>Decision the level implies (NR-01 1.5.4.4.3), shown next to the choice so the
        /// specialist sees the consequence of the classification, not just its name.</summary>
        public static string DecisionHint(RiskLevel level) => level switch
        {
            RiskLevel.Trivial => "Manter a medida existente",
            RiskLevel.Tolerable => "Monitorar",
            RiskLevel.Moderate => "Medida de prevenção com prazo definido",
            RiskLevel.Substantial => "Medida antes de iniciar ou prosseguir a atividade",
            RiskLevel.Intolerable => "Atividade não pode ocorrer até a medida estar implementada",
            _ => string.Empty
        };

        /// <summary>Canonical JSON token ("trivial", "tolerable", …). Lowercased enum name.</summary>
        public static string ToToken(RiskLevel level) => level.ToString().ToLowerInvariant();

        /// <summary>
        /// Parses a level from a JSON token, accepting the retired three-tier vocabulary so
        /// scenarios authored before the matrix still load: minor→Tolerable,
        /// moderate→Moderate, critical→Substantial. That mapping is not arbitrary — it is the
        /// classification the v0 risk matrix independently produced for those same tasks.
        /// </summary>
        public static bool TryParse(string token, out RiskLevel level)
        {
            level = RiskLevel.Moderate;
            if (string.IsNullOrWhiteSpace(token)) return false;

            switch (token.Trim().ToLowerInvariant())
            {
                case "trivial":     level = RiskLevel.Trivial;     return true;
                case "tolerable":
                case "toleravel":
                case "tolerável":
                case "minor":       level = RiskLevel.Tolerable;   return true;
                case "moderate":
                case "moderado":    level = RiskLevel.Moderate;    return true;
                case "substantial":
                case "substancial":
                case "critical":    level = RiskLevel.Substantial; return true;
                case "intolerable":
                case "intoleravel":
                case "intolerável": level = RiskLevel.Intolerable; return true;
                default:            return false;
            }
        }
    }
}
