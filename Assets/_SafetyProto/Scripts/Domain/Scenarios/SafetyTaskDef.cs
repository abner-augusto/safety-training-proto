#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Scenarios
{
    /// <summary>
    /// JSON-backed classification choice for a hazard-classification task's report popup
    /// (<c>"reportOptions": [{ "id": ..., "label": ..., "correct": ... }]</c>).
    /// </summary>
    public sealed class ReportOptionDef : IReportOption
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("label")] public string Label { get; set; } = string.Empty;
        [JsonProperty("correct")] public bool Correct { get; set; }
    }

    /// <summary>
    /// The two graded axes of a task's risk, as authored. Kept as its own object in the JSON
    /// (<c>"risk": { "severity": 5, "probability": 5 }</c>) so the grades read as a pair and
    /// cannot be mistaken for the retired flat <c>"severity"</c> string.
    /// </summary>
    public sealed class RiskGrades
    {
        /// <summary>Magnitude of the worst possible consequence, 1–5 (NR-01 1.5.4.4.4).</summary>
        [JsonProperty("severity")] public int Severity { get; set; }

        /// <summary>Chance of the injury occurring, 1–5 (NR-01 1.5.4.4.5 / 1.5.4.4.5.4).</summary>
        [JsonProperty("probability")] public int Probability { get; set; }
    }

    /// <summary>
    /// JSON-backed, engine-independent <see cref="ISafetyTask"/>. Replaces both the
    /// Unity <c>SafetyTask</c> ScriptableObject (as the runtime source of truth) and
    /// the CLI harness's old <c>InMemorySafetyTask</c>. One model, all hosts.
    /// </summary>
    /// <remarks>
    /// Raw string fields (<see cref="RequiredPpeNames"/>) come straight from JSON and
    /// are validated/converted by <see cref="ScenarioLoader"/> via <see cref="Bind"/>.
    /// The typed <see cref="ISafetyTask.requiredPPE"/> is only valid after binding.
    /// </remarks>
    public sealed class SafetyTaskDef : ISafetyTask
    {
        /// <summary>Raw stable id as authored in JSON. May be empty; <see cref="id"/> falls back to the name.</summary>
        [JsonProperty("id")]
        public string RawId { get; set; } = string.Empty;

        [JsonIgnore]
        public string id => string.IsNullOrWhiteSpace(RawId) ? taskName : RawId;

        [JsonProperty("name")]
        public string taskName { get; set; } = "unnamed";

        [JsonProperty("taskDescription")]
        public string taskDescription { get; set; } = string.Empty;

        [JsonProperty("actionId")]
        public string ActionId { get; set; } = string.Empty;

        /// <summary>Severity and probability grades as authored in JSON
        /// (<c>"risk": { "severity": 5, "probability": 4 }</c>). Null on scenarios written
        /// before the risk matrix, which carry <see cref="RiskLevelName"/> instead.</summary>
        [JsonProperty("risk")]
        public RiskGrades? Grades { get; set; }

        /// <summary>Raw level token as authored in JSON. Retired three-tier names
        /// ("critical" | "moderate" | "minor") are still accepted. Ignored when
        /// <see cref="Grades"/> is present, since the level is derived from the grades.</summary>
        [JsonProperty("severity")]
        public string RiskLevelName { get; set; } = string.Empty;

        [JsonIgnore]
        public RiskAssessment risk { get; private set; } = RiskAssessment.Default;

        [JsonIgnore]
        public RiskLevel riskLevel => risk.Level;

        /// <summary>Raw PPE names as authored in JSON (e.g. "Boots"). Bound to enums by the loader.</summary>
        [JsonProperty("requiredPPE")]
        public List<string> RequiredPpeNames { get; set; } = new();

        [JsonProperty("hintText")]
        public string hintText { get; set; } = string.Empty;

        [JsonProperty("failureAdvice")]
        public string failureAdvice { get; set; } = string.Empty;

        [JsonProperty("ppeAdvice")]
        public string ppeAdvice { get; set; } = string.Empty;

        [JsonProperty("omissionAdvice")]
        public string omissionAdvice { get; set; } = string.Empty;

        /// <summary>Classification choices for a hazard-classification task. Empty for every
        /// other task, which keeps the plain confirm/cancel report flow (see
        /// <see cref="ScenarioLoader"/> for the "exactly one correct option" validation).</summary>
        [JsonProperty("reportOptions")]
        public List<ReportOptionDef> ReportOptions { get; set; } = new();

        /// <summary>Report-popup copy, authored alongside <see cref="ReportOptions"/>. Moved
        /// here from the scene's <c>SafetyIssueReporter</c> inspector fields so the scenario
        /// stays the single source of participant-facing text.</summary>
        [JsonProperty("reportPopupTitle")]
        public string reportPopupTitle { get; set; } = string.Empty;

        [JsonProperty("reportPopupBody")]
        public string reportPopupBody { get; set; } = string.Empty;

        [JsonProperty("reportConfirmLabel")]
        public string reportConfirmLabel { get; set; } = string.Empty;

        [JsonProperty("reportCancelLabel")]
        public string reportCancelLabel { get; set; } = string.Empty;

        [JsonIgnore]
        private readonly List<PPEType> _requiredPpe = new();

        [JsonIgnore]
        IReadOnlyList<PPEType> ISafetyTask.requiredPPE => _requiredPpe;

        [JsonIgnore]
        IReadOnlyList<IReportOption> ISafetyTask.reportOptions => ReportOptions;

        public string ResolveExpectedActionId() =>
            string.IsNullOrWhiteSpace(ActionId) ? string.Empty : ActionId.Trim();

        /// <summary>
        /// Converts <see cref="RequiredPpeNames"/> into typed PPE values, appending a
        /// human-readable message to <paramref name="errors"/> for any unknown name.
        /// Called once by <see cref="ScenarioLoader"/> after deserialization.
        /// </summary>
        internal void Bind(string groupName, List<string> errors)
        {
            // Graded risk wins: it carries the criteria NR-01 1.5.4.4.2.2 wants documented, and
            // the level falls out of it. The level token is the pre-matrix fallback.
            if (Grades != null)
            {
                if (Grades.Severity < RiskAssessment.MinGrade || Grades.Severity > RiskAssessment.MaxGrade ||
                    Grades.Probability < RiskAssessment.MinGrade || Grades.Probability > RiskAssessment.MaxGrade)
                {
                    errors.Add(
                        $"Severidade e probabilidade devem estar entre {RiskAssessment.MinGrade} e " +
                        $"{RiskAssessment.MaxGrade} na tarefa '{taskName}' (grupo '{groupName}'). " +
                        $"Recebido: severidade={Grades.Severity}, probabilidade={Grades.Probability}.");
                }

                risk = RiskAssessment.FromGrades(Grades.Severity, Grades.Probability);
            }
            else if (string.IsNullOrWhiteSpace(RiskLevelName))
            {
                risk = RiskAssessment.Default;
            }
            else if (RiskLevels.TryParse(RiskLevelName, out var level))
            {
                risk = RiskAssessment.FromLevel(level);
            }
            else
            {
                errors.Add(
                    $"Nível de risco desconhecido '{RiskLevelName}' na tarefa '{taskName}' (grupo '{groupName}'). " +
                    "Valores válidos: trivial, tolerable, moderate, substantial, intolerable " +
                    "(ou informe \"risk\": { \"severity\": 1-5, \"probability\": 1-5 }).");
            }

            _requiredPpe.Clear();
            foreach (var name in RequiredPpeNames)
            {
                if (System.Enum.TryParse<PPEType>(name, ignoreCase: true, out var ppe))
                {
                    _requiredPpe.Add(ppe);
                }
                else
                {
                    var valid = string.Join(", ", System.Enum.GetNames(typeof(PPEType)));
                    errors.Add(
                        $"Tipo de EPI desconhecido '{name}' na tarefa '{taskName}' (grupo '{groupName}'). " +
                        $"Valores válidos: {valid}");
                }
            }

            // Empty is valid (keeps today's plain confirm/cancel report flow); a non-empty
            // list must resolve to exactly one correct answer for the analysis to score it.
            if (ReportOptions.Count > 0)
            {
                int correctCount = ReportOptions.Count(o => o.Correct);
                if (correctCount != 1)
                {
                    errors.Add(
                        $"A tarefa '{taskName}' (grupo '{groupName}') deve declarar exatamente uma opção em " +
                        $"'reportOptions' com \"correct\": true (encontradas: {correctCount}).");
                }

                foreach (var option in ReportOptions)
                {
                    if (string.IsNullOrWhiteSpace(option.Id))
                    {
                        errors.Add(
                            $"Uma opção de 'reportOptions' sem 'id' foi encontrada na tarefa '{taskName}' " +
                            $"(grupo '{groupName}').");
                    }
                }
            }
        }
    }
}
