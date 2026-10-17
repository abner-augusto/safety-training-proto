using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SafetyProto.AuthoringApp.Gui.ViewModels;
using SafetyProto.Domain.Capabilities;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.Tests
{
    /// <summary>
    /// <c>TaskViewModel.ToDef()</c> rebuilds a task field by field, so a field it does not map is
    /// silently deleted the next time the author saves.
    /// </summary>
    public class AuthoringViewModelTests
    {
        private const string Scenario =
            "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[" +
            "{\"id\":\"equip_boots\",\"name\":\"Botina\",\"requiredPPE\":[\"Boots\"]," +
            "\"distractors\":[{\"id\":\"flip_flops\",\"name\":\"Chinelo\",\"rejectionAdvice\":\"Expõe os pés.\"}]}]}]}";

        private static ScenarioEditorViewModel Open()
        {
            var load = ScenarioLoader.Parse(Scenario);
            Assert.That(load.Success, Is.True, load.ErrorSummary);
            var catalog = new CapabilityCatalog { PpeTypes = new List<string> { "Boots" } };
            return new ScenarioEditorViewModel(load.Scenario!, catalog);
        }

        [Test]
        public void Distractors_SurviveAnOpenAndSaveWithoutEdits()
        {
            var saved = Open().ToDef().Groups[0].TaskDefs[0].Distractors.Single();

            Assert.AreEqual("flip_flops", saved.Id);
            Assert.AreEqual("Chinelo", saved.Name);
            Assert.AreEqual("Expõe os pés.", saved.RejectionAdvice);
        }

        [Test]
        public void Distractors_CanBeAddedEditedAndRemoved()
        {
            var editor = Open();
            var task = editor.Groups[0].Tasks[0];

            task.AddDistractor();
            var added = task.Distractors.Last();
            added.Id = " abdominal_belt ";
            added.Name = "Cinto abdominal";
            added.RejectionAdvice = "Não retém a queda.";
            task.RemoveDistractor(task.Distractors[0]);

            var saved = editor.ToDef().Groups[0].TaskDefs[0].Distractors.Single();
            Assert.AreEqual("abdominal_belt", saved.Id, "The id is trimmed on save.");
            Assert.AreEqual("Não retém a queda.", saved.RejectionAdvice);
        }
    }
}
