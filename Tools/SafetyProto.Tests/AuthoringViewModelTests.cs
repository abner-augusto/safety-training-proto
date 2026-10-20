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
            "{\"distractors\":[{\"id\":\"flip_flops\",\"name\":\"Chinelo\",\"rejectionAdvice\":\"Expõe os pés.\"}]," +
            "\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[" +
            "{\"id\":\"equip_boots\",\"name\":\"Botina\",\"requiredPPE\":[\"Boots\"],\"distractorIds\":[\"flip_flops\"]}," +
            "{\"id\":\"report_mesh\",\"name\":\"Tela\",\"actionId\":\"flag_safety_net\"," +
            "\"reportPopupTitle\":\"Comunicar\",\"reportPopupBody\":\"Como classificar?\"," +
            "\"reportOptions\":[{\"id\":\"torn\",\"label\":\"Rasgada\",\"correct\":true},{\"id\":\"ok\",\"label\":\"Sem irregularidade\",\"correct\":false}]}]}]}";

        private static ScenarioEditorViewModel Open(string json = Scenario)
        {
            var load = ScenarioLoader.Parse(json);
            Assert.That(load.Success, Is.True, load.ErrorSummary);
            var catalog = new CapabilityCatalog { PpeTypes = new List<string> { "Boots" } };
            return new ScenarioEditorViewModel(load.Scenario!, catalog);
        }

        private static TaskViewModel Boots(ScenarioEditorViewModel editor) => editor.Groups[0].Tasks[0];
        private static TaskViewModel Mesh(ScenarioEditorViewModel editor) => editor.Groups[0].Tasks[1];

        [Test]
        public void Distractors_SurviveAnOpenAndSaveWithoutEdits()
        {
            var saved = Open().ToDef();

            var entry = saved.Distractors.Single();
            Assert.AreEqual("flip_flops", entry.Id);
            Assert.AreEqual("Chinelo", entry.Name);
            Assert.AreEqual("Expõe os pés.", entry.RejectionAdvice);
            CollectionAssert.AreEqual(new[] { "flip_flops" }, saved.Groups[0].TaskDefs[0].DistractorIds);
        }

        [Test]
        public void LegacyInlineDistractors_OpenIntoTheCatalogAndSaveAsLinks()
        {
            var legacy =
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[" +
                "{\"id\":\"equip_boots\",\"name\":\"Botina\",\"requiredPPE\":[\"Boots\"]," +
                "\"distractors\":[{\"id\":\"flip_flops\",\"name\":\"Chinelo\",\"rejectionAdvice\":\"Expõe os pés.\"}]}]}]}";

            var editor = Open(legacy);

            Assert.AreEqual("flip_flops", editor.Distractors.Single().Id);
            Assert.AreSame(editor.Distractors[0], Boots(editor).LinkedDistractors.Single());
            CollectionAssert.AreEqual(new[] { "flip_flops" }, editor.ToDef().Groups[0].TaskDefs[0].DistractorIds);
        }

        [Test]
        public void CatalogEdits_ReachEveryLinkingTask()
        {
            var editor = Open();
            editor.Distractors[0].Id = " chinelo ";

            var saved = editor.ToDef();

            Assert.AreEqual("chinelo", saved.Distractors.Single().Id, "The id is trimmed on save.");
            CollectionAssert.AreEqual(new[] { "chinelo" }, saved.Groups[0].TaskDefs[0].DistractorIds);
        }

        [Test]
        public void LinkingFromTheDropdown_AddsTheCatalogEntryOnce()
        {
            var editor = Open();
            var belt = editor.AddDistractor();
            belt.Id = "abdominal_belt";
            var boots = Boots(editor);

            boots.DistractorToLink = belt;
            boots.LinkDistractor();
            boots.DistractorToLink = belt;
            boots.LinkDistractor();

            CollectionAssert.AreEqual(new[] { "flip_flops", "abdominal_belt" }, boots.ToDef().DistractorIds);
            Assert.IsNull(boots.DistractorToLink);
            Assert.AreEqual("Botina", belt.UsedBy);
        }

        [Test]
        public void RemovingACatalogEntry_UnlinksItFromItsTasks()
        {
            var editor = Open();
            editor.SelectedDistractor = editor.Distractors[0];

            editor.RemoveSelectedDistractor();

            var saved = editor.ToDef();
            Assert.IsEmpty(saved.Distractors);
            Assert.IsEmpty(saved.Groups[0].TaskDefs[0].DistractorIds);
        }

        [Test]
        public void DanglingDistractorId_IsKeptAsABlankCatalogEntry()
        {
            var json =
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[" +
                "{\"id\":\"equip_boots\",\"name\":\"Botina\",\"requiredPPE\":[\"Boots\"],\"distractorIds\":[\"ghost\"]}]}]}";

            var saved = Open(json).ToDef();

            Assert.AreEqual("ghost", saved.Distractors.Single().Id);
            CollectionAssert.AreEqual(new[] { "ghost" }, saved.Groups[0].TaskDefs[0].DistractorIds);
        }

        [Test]
        public void EquipTask_IsOnlyATaskWithoutActionThatRequiresPpe()
        {
            var editor = Open();

            Assert.IsTrue(Boots(editor).IsEquipTask);
            Assert.IsFalse(Mesh(editor).IsEquipTask);

            Boots(editor).PpeOptions.Single().IsSelected = false;
            Assert.IsFalse(Boots(editor).IsEquipTask);
        }

        [Test]
        public void ReportPopup_IsOnWhenTheTaskHasOptionsAndOffOtherwise()
        {
            var editor = Open();

            Assert.IsTrue(Mesh(editor).HasReportPopup);
            Assert.IsFalse(Boots(editor).HasReportPopup);
        }

        [Test]
        public void ReportPopup_TurningItOnSeedsTwoAlternativesWithOneCorrect()
        {
            var boots = Boots(Open());

            boots.HasReportPopup = true;

            Assert.AreEqual(2, boots.ReportOptions.Count);
            Assert.AreEqual(1, boots.ReportOptions.Count(o => o.Correct));
            Assert.AreEqual(TaskViewModel.DefaultReportPopupTitle, boots.ReportPopupTitle);
        }

        [Test]
        public void ReportPopup_OffSavesNoOptionsOrCopy()
        {
            var mesh = Mesh(Open());

            mesh.HasReportPopup = false;
            var saved = mesh.ToDef();

            Assert.IsEmpty(saved.ReportOptions);
            Assert.AreEqual(string.Empty, saved.reportPopupTitle);
            Assert.AreEqual(string.Empty, saved.reportPopupBody);
            Assert.AreEqual(2, mesh.ReportOptions.Count, "Toggling back on restores the alternatives.");
        }

        [Test]
        public void ReportPopup_AllowsAtMostFourAlternatives()
        {
            var mesh = Mesh(Open());

            mesh.AddReportOption();
            mesh.AddReportOption();
            mesh.AddReportOption();

            Assert.AreEqual(4, mesh.ReportOptions.Count);
            Assert.IsFalse(mesh.CanAddReportOption);
        }

        [Test]
        public void ReportPopup_MarkingAnAlternativeCorrectClearsTheOthers()
        {
            var mesh = Mesh(Open());

            mesh.ReportOptions[1].Correct = true;

            Assert.IsFalse(mesh.ReportOptions[0].Correct);
            Assert.AreEqual("ok", mesh.ToDef().ReportOptions.Single(o => o.Correct).Id);
        }

        [Test]
        public void ReportPopup_RemovingTheCorrectAlternativePromotesTheFirst()
        {
            var mesh = Mesh(Open());

            mesh.RemoveReportOption(mesh.ReportOptions[0]);

            Assert.IsTrue(mesh.ReportOptions.Single().Correct);
        }

        [Test]
        public void ReportPopup_BlankIdsAreDerivedFromTheLabelAndKeptUnique()
        {
            var mesh = Mesh(Open());
            mesh.AddReportOption();
            mesh.ReportOptions[2].Label = "Fixação inadequada";
            mesh.AddReportOption();
            mesh.ReportOptions[3].Id = "fixacao_inadequada";

            var ids = mesh.ToDef().ReportOptions.Select(o => o.Id).ToList();

            CollectionAssert.AreEqual(new[] { "torn", "ok", "fixacao_inadequada_2", "fixacao_inadequada" }, ids);
        }
    }
}
