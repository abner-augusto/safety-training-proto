using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SafetyProto.Domain.Scenarios;
using SafetyProto.Runtime.PPE;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// A decoy prefab whose <c>distractorId</c> is blank or unknown falls back to the generic
    /// rejection and charges no task, so the link between scene and scenario is checked here.
    /// </summary>
    public class DistractorPrefabTests
    {
        private const string ScenarioPath = "Assets/_SafetyProto/Resources/Scenarios/default.json";
        private const string PrefabRoot = "Assets/_SafetyProto/Prefabs";
        private const string ScenePath = "Assets/_SafetyProto/Scene/SafetyTraining.unity";

        // A decoy can also be a scene instance of a real PPE prefab with the id set as an
        // override (the helmet without chinstrap). Reading the YAML avoids opening the 2 MB scene.
        private static readonly Regex SceneDistractorOverride =
            new Regex(@"propertyPath: distractorId\r?\n\s+value: (\S+)");

        private static HashSet<string> ScenarioDistractorIds()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(ScenarioPath);
            Assert.IsNotNull(json, $"Missing {ScenarioPath}");
            var load = ScenarioLoader.Parse(json.text);
            Assert.IsTrue(load.Success, load.ErrorSummary);

            return new HashSet<string>(load.Scenario.Groups
                .SelectMany(g => g.TaskDefs)
                .SelectMany(t => t.Distractors)
                .Select(d => d.Id));
        }

        [Test]
        public void EveryDistractorPrefabLinksToAnAuthoredDistractor()
        {
            var authored = ScenarioDistractorIds();
            var checkedPrefabs = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (var item in prefab.GetComponentsInChildren<PPEItem>(true))
                {
                    if (!item.isDistractor) continue;
                    checkedPrefabs++;

                    Assert.IsFalse(string.IsNullOrWhiteSpace(item.distractorId),
                        $"{path} ({item.name}) is a distractor without a distractorId.");
                    Assert.IsTrue(authored.Contains(item.distractorId),
                        $"{path} ({item.name}) links to '{item.distractorId}', which no task in default.json authors.");
                }
            }

            Assert.Greater(checkedPrefabs, 0, "No distractor prefab was found; the search path is stale.");
        }

        [Test]
        public void EveryAuthoredDistractorHasAPrefab()
        {
            var linked = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                foreach (var item in prefab.GetComponentsInChildren<PPEItem>(true))
                    if (item.isDistractor) linked.Add(item.distractorId);
            }
            foreach (Match match in SceneDistractorOverride.Matches(File.ReadAllText(ScenePath)))
                linked.Add(match.Groups[1].Value);

            foreach (var id in ScenarioDistractorIds())
                Assert.IsTrue(linked.Contains(id), $"Distractor '{id}' is authored but no prefab or scene instance links to it.");
        }
    }
}
