#nullable enable
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scoring;

namespace SafetyProto.Domain.Safety
{
    /// <summary>
    /// Scoring/violation consequence of picking a wrong option on a hazard-classification
    /// report popup (see <c>SafetyTaskDef.ReportOptions</c>). The task itself still reaches
    /// <c>CompletedSuccess</c> through the normal action-attempt path — this only adds the
    /// tier's base penalty on top, through the same violation + score-service path the
    /// inspection gate uses for its own charges. No new <c>TaskState</c>, no new scoring knob.
    ///
    /// Kept as a pure, engine-independent policy (no <c>MonoBehaviour</c>) specifically so it
    /// can be unit-tested directly — a <c>MonoBehaviour</c> that only reacts in <c>OnEnable</c>
    /// cannot be reached from EditMode, a trap this project has hit twice before.
    /// </summary>
    public static class HazardClassificationPolicy
    {
        /// <summary>Lead-in of the correction shown after a wrong pick. Provisional wording.</summary>
        public const string CorrectClassificationPrefix = "Classificação correta: ";

        /// <summary>
        /// What the report tells a participant who picked the wrong option: the correct
        /// classification, then the explanation the correct path shows (the task's
        /// <c>failureAdvice</c>).
        /// </summary>
        public static string BuildCorrectionAdvice(ISafetyTask task)
        {
            string label = string.Empty;
            var options = task.reportOptions;
            if (options != null)
            {
                foreach (var option in options)
                {
                    if (!option.Correct) continue;
                    label = option.Label;
                    break;
                }
            }

            string lead = string.IsNullOrWhiteSpace(label) ? string.Empty : $"{CorrectClassificationPrefix}{label}.";
            if (string.IsNullOrWhiteSpace(task.failureAdvice)) return lead;
            return lead.Length == 0 ? task.failureAdvice : $"{lead} {task.failureAdvice}";
        }

        public static void ChargeMisclassification(
            IEventBus bus,
            IScoreService scoreService,
            ScoringConfig scoring,
            ISafetyTask? task,
            ITaskGroup? group,
            string reporterName)
        {
            bus.Publish(new SafetyViolationEventArgs
            {
                ViolationCode = ViolationCodes.HazardMisclassified,
                Message = $"Classificação incorreta reportada em '{reporterName}'.",
                TaskId = task?.id ?? string.Empty,
                GroupId = group?.id ?? string.Empty,
                TaskName = task?.taskName ?? string.Empty,
                GroupName = group?.groupName ?? string.Empty
            });

            int penalty = scoring.BasePenaltyFor(task?.riskLevel ?? RiskLevel.Moderate);
            if (penalty > 0)
                scoreService.SubtractPoints(penalty, ViolationCodes.HazardMisclassified, task?.id ?? string.Empty);
        }
    }
}
