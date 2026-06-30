#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Domain.Scenarios;
using SafetyProto.Runtime.Task;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Editor
{
    /// <summary>
    /// One-shot (re-)bake of the ScriptableObject authoring data into the unified scenario
    /// JSON consumed at runtime by <see cref="ScenarioSource"/>. Reads the TaskManager in the
    /// open scene and writes <c>Assets/_SafetyProto/Resources/Scenarios/&lt;name&gt;.json</c>.
    /// Transitional: lives until authoring moves to the desktop app and the SOs are removed.
    /// </summary>
    public static class ScenarioExporter
    {
        private const string ResourcesDir = "Assets/_SafetyProto/Resources/Scenarios";

        [MenuItem("SafetyProto/Bake Scene Scenario to JSON")]
        public static void BakeOpenScene()
        {
            var taskManager = Object.FindFirstObjectByType<TaskManager>();
            if (taskManager == null)
            {
                EditorUtility.DisplayDialog("Bake Scenario",
                    "Nenhum TaskManager encontrado na cena aberta.", "OK");
                return;
            }

            var scenario = BuildScenario(taskManager.taskGroups, name: "default", participantId: "P000");
            var json = JsonConvert.SerializeObject(scenario, Formatting.Indented);

            Directory.CreateDirectory(ResourcesDir);
            var path = Path.Combine(ResourcesDir, scenario.Name + ".json");
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();

            // Validate the just-written file through the same loader the runtime uses.
            var result = ScenarioLoader.Parse(json);
            if (!result.Success || result.Scenario == null)
            {
                Debug.LogError($"[ScenarioExporter] Baked '{path}' but it failed validation: {result.ErrorSummary}");
                return;
            }

            int groups = result.Scenario.Groups.Count;
            int tasks = result.Scenario.Groups.Sum(g => g.TaskDefs.Count);
            Debug.Log($"[ScenarioExporter] Baked '{path}' OK — {groups} groups, {tasks} tasks.");
        }

        private static ScenarioDef BuildScenario(IReadOnlyList<TaskGroup> groups, string name, string participantId)
        {
            var scenario = new ScenarioDef { Name = name, ParticipantId = participantId };

            foreach (var g in groups)
            {
                if (g == null) continue;

                var groupDef = new TaskGroupDef
                {
                    groupName = g.groupName,
                    ExecutionModeName = g.executionMode.ToString(),
                    timeLimit = g.timeLimit,
                    RequiredGroupNames = (g.requiredGroups ?? new List<TaskGroup>())
                        .Where(rg => rg != null)
                        .Select(rg => rg.groupName)
                        .ToList(),
                };

                foreach (var t in g.tasks)
                {
                    if (t == null) continue;

                    groupDef.TaskDefs.Add(new SafetyTaskDef
                    {
                        taskName = t.taskName,
                        taskDescription = t.taskDescription,
                        ActionId = t.ResolveExpectedActionId(),
                        successPoints = t.successPoints,
                        failurePenalty = t.failurePenalty,
                        ppePenalty = t.ppePenalty,
                        RequiredPpeNames = (t.requiredPPE ?? new List<SafetyProto.Core.PPEType>())
                            .Select(p => p.ToString())
                            .ToList(),
                        hintText = t.hintText,
                        failureAdvice = t.failureAdvice,
                        ppeAdvice = t.ppeAdvice,
                    });
                }

                scenario.Groups.Add(groupDef);
            }

            return scenario;
        }
    }
}
