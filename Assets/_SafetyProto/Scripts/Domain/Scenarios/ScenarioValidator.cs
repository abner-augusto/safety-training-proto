#nullable enable
using System;
using System.Collections.Generic;
using SafetyProto.Domain.Capabilities;

namespace SafetyProto.Domain.Scenarios
{
    /// <summary>
    /// Catalog-aware semantic validation: checks that a scenario only references actions,
    /// PPE, and dependencies that the target build actually implements (per a
    /// <see cref="CapabilityCatalog"/>). This is the contract that lets a safety specialist
    /// author scenarios in the desktop app and be sure they will run. The engine does not
    /// run it; the authoring tools are the gate.
    /// </summary>
    /// <remarks>
    /// Complements <see cref="ScenarioLoader"/>: the loader guarantees the document is
    /// structurally valid (parses, known PPE/mode enums, resolvable group deps); the
    /// validator additionally guarantees it is <em>realizable</em> against a concrete build.
    /// </remarks>
    public static class ScenarioValidator
    {
        public static IReadOnlyList<string> Validate(ScenarioDef scenario, CapabilityCatalog catalog)
        {
            var errors = new List<string>();
            if (scenario == null)
            {
                errors.Add("Cenário nulo.");
                return errors;
            }
            if (catalog == null)
            {
                errors.Add("Catálogo de capacidades nulo.");
                return errors;
            }

            var actions = new HashSet<string>(catalog.ActionIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var ppe = new HashSet<string>(catalog.PpeTypes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var distractorIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var group in scenario.Groups)
            {
                foreach (var task in group.TaskDefs)
                {
                    var actionId = task.ResolveExpectedActionId();
                    bool hasAction = !string.IsNullOrEmpty(actionId);

                    if (hasAction && !actions.Contains(actionId))
                    {
                        errors.Add(
                            $"Ação '{actionId}' (tarefa '{task.taskName}', grupo '{group.groupName}') " +
                            $"não existe no catálogo de capacidades.");
                    }

                    foreach (var name in task.RequiredPpeNames)
                    {
                        if (!ppe.Contains(name))
                        {
                            errors.Add(
                                $"EPI '{name}' (tarefa '{task.taskName}', grupo '{group.groupName}') " +
                                $"não está disponível no catálogo.");
                        }
                    }

                    if (!hasAction && task.RequiredPpeNames.Count == 0)
                    {
                        errors.Add(
                            $"Tarefa '{task.taskName}' (grupo '{group.groupName}') não define ação " +
                            $"nem EPI requerido — nunca poderá ser concluída.");
                    }

                    ValidateDistractors(task, group.groupName, hasAction, distractorIds, errors);

                    if (task.successAdviceNarrated && string.IsNullOrWhiteSpace(task.successAdvice))
                    {
                        errors.Add(
                            $"A tarefa '{task.taskName}' (grupo '{group.groupName}') declara narra\u00e7\u00e3o " +
                            "de dica de sucesso sem 'successAdvice'.");
                    }
                }
            }

            return errors;
        }

        private static void ValidateDistractors(
            SafetyTaskDef task, string groupName, bool hasAction, HashSet<string> seenIds, List<string> errors)
        {
            if (task.Distractors.Count == 0) return;

            if (hasAction || task.RequiredPpeNames.Count == 0)
            {
                errors.Add(
                    $"Distratores só podem ser declarados em tarefas de equipar EPI (com EPI requerido e sem ação): " +
                    $"tarefa '{task.taskName}', grupo '{groupName}'.");
            }

            foreach (var distractor in task.Distractors)
            {
                var id = distractor.Id?.Trim() ?? string.Empty;
                if (id.Length == 0)
                {
                    errors.Add($"Distrator sem 'id' na tarefa '{task.taskName}' (grupo '{groupName}').");
                }
                else if (!seenIds.Add(id))
                {
                    errors.Add(
                        $"O id de distrator '{id}' se repete (tarefa '{task.taskName}', grupo '{groupName}'); " +
                        $"cada id deve ser único no cenário.");
                }

                if (string.IsNullOrWhiteSpace(distractor.RejectionAdvice))
                {
                    errors.Add(
                        $"O distrator '{id}' da tarefa '{task.taskName}' (grupo '{groupName}') " +
                        $"não tem 'rejectionAdvice'.");
                }
            }
        }
    }
}
