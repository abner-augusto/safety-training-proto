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
    /// author scenarios in the desktop app and be sure they will run — the same check
    /// runs there and (optionally) at load time in the engine.
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

                    // An equip-set task (no action) completes on PPE; an action task needs an action.
                    // A task with neither can never complete.
                    if (!hasAction && task.RequiredPpeNames.Count == 0)
                    {
                        errors.Add(
                            $"Tarefa '{task.taskName}' (grupo '{group.groupName}') não define ação " +
                            $"nem EPI requerido — nunca poderá ser concluída.");
                    }
                }
            }

            return errors;
        }
    }
}
