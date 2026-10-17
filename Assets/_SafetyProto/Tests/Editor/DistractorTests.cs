using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Capabilities;
using SafetyProto.Domain.Safety;
using SafetyProto.Domain.Scenarios;
using SafetyProto.Domain.Scoring;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class DistractorTests
    {
        private static readonly CapabilityCatalog Catalog = new CapabilityCatalog
        {
            ActionIds = new System.Collections.Generic.List<string> { "flag_safety_net" },
            PpeTypes = new System.Collections.Generic.List<string> { "Boots", "Harness", "GloveLeft", "GloveRight" },
        };

        private const string BootsWithDistractor =
            "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[" +
            "{\"id\":\"equip_boots\",\"name\":\"Botina\",\"requiredPPE\":[\"Boots\"],\"hintText\":\"Escolha a botina.\"," +
            "\"distractors\":[{\"id\":\"flip_flops\",\"name\":\"Chinelo\",\"rejectionAdvice\":\"Expõe os pés.\"}]}]}]}";

        private static System.Collections.Generic.IReadOnlyList<string> Validate(string json)
        {
            var load = ScenarioLoader.Parse(json);
            Assert.That(load.Success, Is.True, load.ErrorSummary);
            return ScenarioValidator.Validate(load.Scenario!, Catalog);
        }

        private static FakeTaskBuilder.FakeSafetyTask EquipTask(string id, string hint, params FakeTaskBuilder.FakeDistractor[] decoys)
        {
            var task = new FakeTaskBuilder().Task(id, string.Empty, PPEType.Boots);
            task.hintText = hint;
            task.Distractors.AddRange(decoys);
            return task;
        }

        private static FakeTaskBuilder.FakeDistractor Decoy(string id, string name, string advice) =>
            new FakeTaskBuilder.FakeDistractor { Id = id, Name = name, RejectionAdvice = advice };

        [Test]
        public void Resolve_FindsTheDecoyAndItsOwningTaskAcrossGroups()
        {
            var tasks = new FakeTaskBuilder();
            var boots = EquipTask("equip_boots", "Dica botina", Decoy("flip_flops", "Chinelo", "Expõe os pés."));
            var gloves = EquipTask("equip_gloves", "Dica luvas", Decoy("rubber_gloves", "Luva de borracha", "Não protege."));
            var groups = new[]
            {
                tasks.Group("a", TaskExecutionModeShared.Sequential, boots),
                tasks.Group("b", TaskExecutionModeShared.Sequential, gloves),
            };

            var match = DistractorResolver.Resolve(groups, "rubber_gloves");

            Assert.IsNotNull(match);
            Assert.AreSame(gloves, match!.Task);
            Assert.AreEqual("Luva de borracha", match.Distractor.Name);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("nope")]
        public void Resolve_ReturnsNullForEmptyOrUnknownIds(string id)
        {
            var groups = new[]
            {
                new FakeTaskBuilder().Group("a", TaskExecutionModeShared.Sequential,
                    EquipTask("equip_boots", "Dica", Decoy("flip_flops", "Chinelo", "Expõe os pés."))),
            };

            Assert.IsNull(DistractorResolver.Resolve(groups, id));
            Assert.IsNull(DistractorResolver.Resolve(null, "flip_flops"));
        }

        [Test]
        public void BuildRejectionBody_IsAdviceThenTheOwningTasksHint()
        {
            var task = EquipTask("equip_boots", "Escolha a botina.", Decoy("flip_flops", "Chinelo", "Expõe os pés."));
            var match = new DistractorMatch(task, task.Distractors[0]);

            Assert.AreEqual("Expõe os pés.\n\nDica: Escolha a botina.", DistractorResolver.BuildRejectionBody(match));
        }

        [Test]
        public void BuildRejectionBody_OmitsTheHintLineWhenTheTaskHasNone()
        {
            var task = EquipTask("equip_boots", "", Decoy("flip_flops", "Chinelo", "Expõe os pés."));
            var match = new DistractorMatch(task, task.Distractors[0]);

            Assert.AreEqual("Expõe os pés.", DistractorResolver.BuildRejectionBody(match));
        }

        [Test]
        public void BuildRejectionBody_NeverReadsPpeAdvice()
        {
            var task = EquipTask("equip_boots", "Dica", Decoy("flip_flops", "Chinelo", "Expõe os pés."));
            task.ppeAdvice = "Texto de outra situação.";
            var match = new DistractorMatch(task, task.Distractors[0]);

            StringAssert.DoesNotContain("outra situação", DistractorResolver.BuildRejectionBody(match));
        }

        [Test]
        public void Json_RoundTripsDistractors()
        {
            var load = ScenarioLoader.Parse(BootsWithDistractor);
            Assert.That(load.Success, Is.True, load.ErrorSummary);

            var task = load.Scenario!.Groups[0].TaskDefs[0];
            Assert.AreEqual(1, task.Distractors.Count);
            Assert.AreEqual("flip_flops", task.Distractors[0].Id);
            Assert.AreEqual("Chinelo", task.Distractors[0].Name);

            var again = ScenarioLoader.Parse(JsonConvert.SerializeObject(load.Scenario));
            Assert.That(again.Success, Is.True, again.ErrorSummary);
            var copy = again.Scenario!.Groups[0].TaskDefs[0].Distractors.Single();
            Assert.AreEqual("flip_flops", copy.Id);
            Assert.AreEqual("Expõe os pés.", copy.RejectionAdvice);
        }

        [Test]
        public void Validator_AcceptsADistractorOnAnEquipTask()
        {
            Assert.IsEmpty(Validate(BootsWithDistractor));
        }

        [Test]
        public void Validator_RejectsDuplicateIdsAcrossTasks()
        {
            var json =
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[" +
                "{\"id\":\"a\",\"name\":\"a\",\"requiredPPE\":[\"Boots\"],\"distractors\":[{\"id\":\"x\",\"name\":\"X\",\"rejectionAdvice\":\"r\"}]}," +
                "{\"id\":\"b\",\"name\":\"b\",\"requiredPPE\":[\"Harness\"],\"distractors\":[{\"id\":\"x\",\"name\":\"X\",\"rejectionAdvice\":\"r\"}]}]}]}";

            var errors = Validate(json);

            Assert.That(errors.Any(e => e.Contains("'x'") && e.Contains("único")), Is.True, string.Join(" | ", errors));
        }

        [Test]
        public void Validator_RejectsEmptyAdviceAndEmptyId()
        {
            var json =
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[" +
                "{\"id\":\"a\",\"name\":\"a\",\"requiredPPE\":[\"Boots\"],\"distractors\":[" +
                "{\"id\":\"x\",\"name\":\"X\",\"rejectionAdvice\":\"  \"},{\"id\":\"\",\"name\":\"Y\",\"rejectionAdvice\":\"r\"}]}]}]}";

            var errors = Validate(json);

            Assert.That(errors.Any(e => e.Contains("rejectionAdvice")), Is.True, string.Join(" | ", errors));
            Assert.That(errors.Any(e => e.Contains("sem 'id'")), Is.True, string.Join(" | ", errors));
        }

        [Test]
        public void Validator_RejectsDistractorsOnATaskWithAnAction()
        {
            var json =
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[" +
                "{\"id\":\"a\",\"name\":\"a\",\"actionId\":\"flag_safety_net\",\"requiredPPE\":[\"Boots\"]," +
                "\"distractors\":[{\"id\":\"x\",\"name\":\"X\",\"rejectionAdvice\":\"r\"}]}]}]}";

            var errors = Validate(json);

            Assert.That(errors.Any(e => e.Contains("tarefas de equipar EPI")), Is.True, string.Join(" | ", errors));
        }

        [Test]
        public void Validator_RejectsDistractorsOnATaskWithoutRequiredPpe()
        {
            var json =
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[" +
                "{\"id\":\"a\",\"name\":\"a\",\"actionId\":\"flag_safety_net\"," +
                "\"distractors\":[{\"id\":\"x\",\"name\":\"X\",\"rejectionAdvice\":\"r\"}]}]}]}";

            Assert.That(Validate(json).Any(e => e.Contains("tarefas de equipar EPI")), Is.True);
        }

        [Test]
        public void WrongPpeCharge_IsAttributedToTheResolvedTaskAndNamesTheDecoy()
        {
            var bus = new FakeEventBus();
            var score = new ScoreService();
            SafetyViolationEventArgs? seen = null;
            bus.Subscribe<SafetyViolationEventArgs>(args => seen = args);

            IncidentalChargePolicy.ChargeWrongPpeSelected(
                bus, score, ScoringConfig.Default, "Chinelo", "Distractor_FlipFlops", firstAttemptForItem: true,
                taskId: "equip_boots", taskName: "Equipar Botina");

            Assert.IsNotNull(seen);
            Assert.AreEqual("equip_boots", seen!.Value.TaskId);
            Assert.AreEqual("Equipar Botina", seen.Value.TaskName);
            StringAssert.EndsWith("Chinelo", seen.Value.Message);
            var charge = score.Ledger.Charges.Single();
            Assert.AreEqual("equip_boots", charge.TaskId);
            Assert.AreEqual(ViolationCodes.WrongPpeSelected, charge.Code);
        }

        [Test]
        public void WrongPpeCharge_RepeatAttemptStillLogsButChargesOnce()
        {
            var bus = new FakeEventBus();
            var score = new ScoreService();
            int violations = 0;
            bus.Subscribe<SafetyViolationEventArgs>(_ => violations++);

            IncidentalChargePolicy.ChargeWrongPpeSelected(bus, score, ScoringConfig.Default, "Chinelo", "d", true, "equip_boots");
            IncidentalChargePolicy.ChargeWrongPpeSelected(bus, score, ScoringConfig.Default, "Chinelo", "d", false, "equip_boots");

            Assert.AreEqual(2, violations);
            Assert.AreEqual(1, score.Ledger.Charges.Count);
        }
    }
}
