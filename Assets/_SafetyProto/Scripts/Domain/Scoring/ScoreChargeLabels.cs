#nullable enable
using System.Collections.Generic;
using SafetyProto.Core;

namespace SafetyProto.Domain.Scoring
{
    /// <summary>
    /// Portuguese names of the reasons a charge can carry, as the participant reads them on the
    /// finish report. The wording matches the evaluator dashboard's violation labels so both
    /// surfaces name a penalty the same way.
    /// </summary>
    public static class ScoreChargeLabels
    {
        /// <summary>Shown for charges that belong to no task row.</summary>
        public const string OtherChargesLabel = "Outros descontos";

        private static readonly Dictionary<string, string> Labels = new()
        {
            [ViolationCodes.ActionIdMissing] = "Ação não identificada",
            [ViolationCodes.NoActiveGroup] = "Nenhum grupo de tarefas ativo",
            [ViolationCodes.WrongAction] = "Ação incorreta",
            [ViolationCodes.PpeMissing] = "EPI obrigatório ausente",
            [ViolationCodes.TaskNotPerformed] = "Tarefa não realizada",
            [ViolationCodes.GateFailed] = "Inspeção não aprovada",
            [ViolationCodes.WrongPpeSelected] = "Equipamento inadequado selecionado",
            [ViolationCodes.InspectionIncomplete] = "Inspeção incompleta",
            [ViolationCodes.OrderViolation] = "Ordem recomendada não seguida",
            [ViolationCodes.PpeOutOfOrder] = "EPI fora de ordem",
            [ViolationCodes.HazardMisclassified] = "Classificação incorreta da irregularidade",
            // The gate's per-task charge is a score reason, not a violation; the violation raised
            // beside it is GateFailed or InspectionIncomplete, so it reads as the failed inspection.
            [ViolationCodes.GatePenalty] = "Inspeção não aprovada",
        };

        /// <summary>One report line for a charge, e.g. "Equipamento inadequado selecionado: -30 pts".</summary>
        public static string FormatLine(ScoreCharge charge) => $"{For(charge.Code)}: -{charge.Amount} pts";

        /// <summary>The Portuguese label for <paramref name="code"/>, or the code itself when unknown.</summary>
        public static string For(string code) =>
            Labels.TryGetValue(code ?? string.Empty, out var label) ? label : code ?? string.Empty;
    }
}
