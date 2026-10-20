#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
        public const int MinReportOptions = 2;
        public const int MaxReportOptions = 4;

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
            var linkedBy = ValidateDistractorCatalog(scenario, errors);

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

                    ValidateDistractorLinks(task, group.groupName, hasAction, linkedBy, errors);
                    ValidateReportOptions(task, group.groupName, errors);

                    if (task.successAdviceNarrated && string.IsNullOrWhiteSpace(task.successAdvice))
                    {
                        errors.Add(
                            $"A tarefa '{task.taskName}' (grupo '{group.groupName}') declara narra\u00e7\u00e3o " +
                            "de dica de sucesso sem 'successAdvice'.");
                    }
                }
            }

            foreach (var entry in linkedBy)
            {
                if (entry.Value.Count == 0)
                {
                    errors.Add(
                        $"O distrator '{entry.Key}' está no catálogo mas não está vinculado a nenhuma tarefa " +
                        "de equipar EPI.");
                }
                else if (entry.Value.Count > 1)
                {
                    errors.Add(
                        $"O distrator '{entry.Key}' está vinculado a mais de uma tarefa " +
                        $"({string.Join(", ", entry.Value)}); cada distrator pertence a uma única tarefa.");
                }
            }

            return errors;
        }

        /// <summary>Checks the scenario-level catalog entries and returns, per valid id, the
        /// list the task loop fills with the names of the tasks linking to it.</summary>
        private static Dictionary<string, List<string>> ValidateDistractorCatalog(ScenarioDef scenario, List<string> errors)
        {
            var linkedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var distractor in scenario.Distractors ?? new List<DistractorDef>())
            {
                var id = distractor.Id?.Trim() ?? string.Empty;
                if (id.Length == 0)
                {
                    errors.Add($"Distrator sem 'id' no catálogo de distratores ('{distractor.Name}').");
                }
                else if (linkedBy.ContainsKey(id))
                {
                    errors.Add($"O id de distrator '{id}' se repete no catálogo; cada id deve ser único no cenário.");
                }
                else
                {
                    linkedBy[id] = new List<string>();
                }

                if (string.IsNullOrWhiteSpace(distractor.RejectionAdvice))
                    errors.Add($"O distrator '{id}' não tem 'rejectionAdvice'.");
            }

            return linkedBy;
        }

        private static void ValidateDistractorLinks(
            SafetyTaskDef task, string groupName, bool hasAction, Dictionary<string, List<string>> linkedBy, List<string> errors)
        {
            if (task.DistractorIds.Count == 0) return;

            if (hasAction || task.RequiredPpeNames.Count == 0)
            {
                errors.Add(
                    $"Distratores só podem ser declarados em tarefas de equipar EPI (com EPI requerido e sem ação): " +
                    $"tarefa '{task.taskName}', grupo '{groupName}'.");
            }

            foreach (var raw in task.DistractorIds)
            {
                var id = raw?.Trim() ?? string.Empty;
                if (linkedBy.TryGetValue(id, out var tasks))
                {
                    if (!tasks.Contains(task.taskName)) tasks.Add(task.taskName);
                }
                else
                {
                    errors.Add(
                        $"A tarefa '{task.taskName}' (grupo '{groupName}') usa o distrator '{id}', " +
                        "que não está no catálogo de distratores.");
                }
            }
        }

        /// <summary>The report popup offers one button per option; the panel lays out at most
        /// <see cref="MaxReportOptions"/>, and a single option would be no choice at all.</summary>
        private static void ValidateReportOptions(SafetyTaskDef task, string groupName, List<string> errors)
        {
            var options = task.ReportOptions;
            if (options.Count == 0) return;

            if (options.Count < MinReportOptions || options.Count > MaxReportOptions)
            {
                errors.Add(
                    $"O popup de reporte da tarefa '{task.taskName}' (grupo '{groupName}') deve ter de " +
                    $"{MinReportOptions} a {MaxReportOptions} alternativas (encontradas: {options.Count}).");
            }

            if (string.IsNullOrWhiteSpace(task.reportPopupTitle) || string.IsNullOrWhiteSpace(task.reportPopupBody))
            {
                errors.Add(
                    $"O popup de reporte da tarefa '{task.taskName}' (grupo '{groupName}') precisa de título " +
                    "e pergunta; a cena não tem texto próprio para o popup.");
            }

            if (options.Any(o => string.IsNullOrWhiteSpace(o.Label)))
            {
                errors.Add(
                    $"O popup de reporte da tarefa '{task.taskName}' (grupo '{groupName}') tem alternativa sem texto.");
            }

            var duplicate = options
                .Select(o => o.Id?.Trim() ?? string.Empty)
                .Where(id => id.Length > 0)
                .GroupBy(id => id, StringComparer.Ordinal)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                errors.Add(
                    $"O id de alternativa '{duplicate.Key}' se repete no popup de reporte da tarefa " +
                    $"'{task.taskName}' (grupo '{groupName}').");
            }
        }
    }
}
