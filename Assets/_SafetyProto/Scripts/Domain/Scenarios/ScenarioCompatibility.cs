#nullable enable
using System;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Domain.Scoring;

namespace SafetyProto.Domain.Scenarios
{
    /// <summary>Checks that an external script describes the scenario loaded by Unity.</summary>
    public sealed class ScenarioCompatibilityResult
    {
        public bool Compatible { get; }
        public IReadOnlyList<string> Errors { get; }

        internal ScenarioCompatibilityResult(bool compatible, IReadOnlyList<string> errors)
        {
            Compatible = compatible;
            Errors = errors;
        }

        public string ErrorSummary => string.Join(" | ", Errors);
    }

    public static class ScenarioCompatibility
    {
        public static ScenarioCompatibilityResult Validate(ScenarioDef candidate, ScenarioDef loaded)
        {
            var errors = new List<string>();
            var groups = new Dictionary<string, TaskGroupDef>(StringComparer.Ordinal);
            foreach (var group in loaded.Groups)
                groups[group.id] = group;

            if (candidate.Groups.Count != groups.Count)
                errors.Add("A quantidade de grupos do cenário selecionado não coincide com o cenário carregado.");
            else
            {
                for (int i = 0; i < candidate.Groups.Count; i++)
                {
                    if (!string.Equals(candidate.Groups[i].id, loaded.Groups[i].id, StringComparison.Ordinal))
                    {
                        errors.Add("A ordem dos grupos do cenário selecionado não coincide com a carregada.");
                        break;
                    }
                }
            }

            foreach (var candidateGroup in candidate.Groups)
            {
                if (!groups.TryGetValue(candidateGroup.id, out var loadedGroup))
                {
                    errors.Add($"Grupo '{candidateGroup.id}' não está carregado pelo TaskManager.");
                    continue;
                }

                if (candidateGroup.executionMode != loadedGroup.executionMode)
                    errors.Add($"O modo de execução do grupo '{candidateGroup.id}' não coincide com o carregado.");
                if (Math.Abs(candidateGroup.timeLimit - loadedGroup.timeLimit) > 0.001f)
                    errors.Add($"O limite de tempo do grupo '{candidateGroup.id}' não coincide com o carregado.");
                if (!SameStrings(candidateGroup.RequiredGroupNames, loadedGroup.RequiredGroupNames))
                    errors.Add($"As dependências do grupo '{candidateGroup.id}' não coincidem com as carregadas.");

                var tasks = new Dictionary<string, SafetyTaskDef>(StringComparer.Ordinal);
                foreach (var task in loadedGroup.TaskDefs)
                    tasks[task.id] = task;

                foreach (var candidateTask in candidateGroup.TaskDefs)
                {
                    if (!tasks.TryGetValue(candidateTask.id, out var loadedTask))
                    {
                        errors.Add($"Tarefa '{candidateTask.id}' não está carregada no grupo '{candidateGroup.id}'.");
                        continue;
                    }

                    if (!string.Equals(candidateTask.ResolveExpectedActionId(), loadedTask.ResolveExpectedActionId(),
                            StringComparison.OrdinalIgnoreCase))
                        errors.Add($"A ação da tarefa '{candidateTask.id}' não coincide com a carregada.");
                    if (!candidateTask.risk.Equals(loadedTask.risk))
                        errors.Add($"O nível de risco da tarefa '{candidateTask.id}' não coincide com o carregado.");
                    if (!SameStrings(candidateTask.RequiredPpeNames, loadedTask.RequiredPpeNames))
                        errors.Add($"Os EPIs da tarefa '{candidateTask.id}' não coincidem com os carregados.");
                }

                if (candidateGroup.TaskDefs.Count != tasks.Count)
                    errors.Add($"A quantidade de tarefas do grupo '{candidateGroup.id}' não coincide com a carregada.");
                else
                {
                    for (int i = 0; i < candidateGroup.TaskDefs.Count; i++)
                    {
                        if (!string.Equals(candidateGroup.TaskDefs[i].id, loadedGroup.TaskDefs[i].id,
                                StringComparison.Ordinal))
                        {
                            errors.Add($"A ordem das tarefas do grupo '{candidateGroup.id}' não coincide com a carregada.");
                            break;
                        }
                    }
                }
            }

            foreach (var loadedGroup in loaded.Groups)
            {
                bool present = false;
                foreach (var candidateGroup in candidate.Groups)
                    if (string.Equals(candidateGroup.id, loadedGroup.id, StringComparison.Ordinal))
                        present = true;
                if (!present)
                    errors.Add($"Grupo carregado '{loadedGroup.id}' não existe no cenário selecionado.");
            }

            if (!SameScoring(candidate.Scoring, loaded.Scoring))
                errors.Add("A configuração de pontuação do cenário selecionado não coincide com a carregada.");

            foreach (var step in candidate.Script)
            {
                if (!string.Equals(step.Kind, "action", StringComparison.OrdinalIgnoreCase)) continue;
                bool found = false;
                foreach (var group in loaded.Groups)
                {
                    foreach (var task in group.TaskDefs)
                    {
                        if (string.Equals(task.ResolveExpectedActionId(), step.ActionId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }
                if (!found)
                    errors.Add($"Ação '{step.ActionId}' do roteiro não existe nas tarefas carregadas.");
            }

            return new ScenarioCompatibilityResult(errors.Count == 0, errors);
        }

        private static bool SameStrings(IReadOnlyList<string> first, IReadOnlyList<string> second)
        {
            if (first.Count != second.Count) return false;
            var values = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase);
            return values.SetEquals(second);
        }

        // Compares the resolved economy per risk level rather than the raw JSON block, so a
        // scenario that spells its levels out and one that inherits them from the defaults are
        // recognised as the same economy.
        private static bool SameScoring(ScoringConfig first, ScoringConfig second)
        {
            if (!first.GateReductionFactor.Equals(second.GateReductionFactor)) return false;

            foreach (var level in RiskLevels.All)
            {
                if (first.PointsFor(level) != second.PointsFor(level)) return false;
                if (first.BasePenaltyFor(level) != second.BasePenaltyFor(level)) return false;
                if (!first.UnsafeFactorFor(level).Equals(second.UnsafeFactorFor(level))) return false;
            }

            return true;
        }
    }
}
