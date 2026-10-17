#nullable enable
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Scoring
{
    /// <summary>
    /// Charges that belong to no task outcome: the participant did something the scenario
    /// does not model as a task (picked a decoy, pressed the gate too early). An order
    /// deviation is recorded here too, but costs nothing. Each method publishes the violation that explains the charge and applies the
    /// amount <see cref="ScoringConfig"/> derives for it. The hosts decide WHEN one of these
    /// happened; this class decides what it costs, so a headless run charges exactly what the
    /// headset does. Modelled on <see cref="Safety.HazardClassificationPolicy"/>.
    /// </summary>
    public static class IncidentalChargePolicy
    {
        /// <summary>Phase-advance gate found tasks completed out of the authored order. Logged as
        /// a violation for analysis only: the participant is never told the order in Evaluation,
        /// so it must not cost points. No-op when <paramref name="deviations"/> is empty.</summary>
        public static void RecordOrderDeviation(IEventBus bus, string groupId, string groupName,
                                                IReadOnlyList<string> deviations)
        {
            if (deviations == null || deviations.Count == 0) return;
            string list = string.Join(", ", deviations);
            bus.Publish(new SafetyViolationEventArgs
            {
                ViolationCode = ViolationCodes.OrderViolation,
                Message = $"EPIs equipados fora da ordem recomendada: {list}",
                TaskId = string.Empty,
                GroupId = groupId ?? string.Empty,
                TaskName = string.Empty,
                GroupName = groupName ?? string.Empty
            });
        }

        /// <summary>A distractor item was snapped. The violation is always published (decoy
        /// selection is a retention measure the log must keep); the charge applies only when
        /// <paramref name="firstAttemptForItem"/> — the host tracks which items it already
        /// charged, so fumbling the same decoy repeatedly cannot spiral.
        /// <paramref name="ppeLabel"/> is what the message names (the decoy's authored name when
        /// it resolved); <paramref name="taskId"/> is the equip task that authored the decoy, empty
        /// when it did not resolve, and <paramref name="taskName"/> is that task's name (the item's
        /// own name stands in when it is omitted).</summary>
        public static int ChargeWrongPpeSelected(IEventBus bus, IScoreService scoreService, ScoringConfig scoring,
                                                 string ppeLabel, string itemName, bool firstAttemptForItem,
                                                 string taskId = "", string? taskName = null)
        {
            taskId ??= string.Empty;
            bus.Publish(new SafetyViolationEventArgs
            {
                ViolationCode = ViolationCodes.WrongPpeSelected,
                Message = $"Selecionou equipamento inadequado: {ppeLabel}",
                TaskId = taskId,
                GroupId = string.Empty,
                TaskName = taskName ?? itemName ?? string.Empty,
                GroupName = string.Empty
            });
            if (!firstAttemptForItem) return 0;
            int charge = scoring.BasePenaltyFor(RiskLevels.IncidentalChargeTier);
            if (charge > 0) scoreService.SubtractPoints(charge, ViolationCodes.WrongPpeSelected, taskId);
            return charge;
        }

        /// <summary>One pending task caught at a failed inspection-gate press. Charge only — the
        /// gate publishes its own GATE_FAILED / INSPECTION_INCOMPLETE violation alongside the
        /// consequence it plays, because which one applies depends on the scene's mapping.</summary>
        public static int ChargeGatePenalty(IScoreService scoreService, ScoringConfig scoring, ISafetyTask task)
        {
            int charge = scoring.GateChargeFor(task.riskLevel);
            if (charge > 0) scoreService.SubtractPoints(charge, ViolationCodes.GatePenalty, task.id);
            return charge;
        }
    }
}
