using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Safety;
using SafetyProto.Domain.Scoring;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// The finish report's logic: which penalties the score ledger kept, how they are labelled,
    /// whether a wrong classification still reads as clean, and when the report may appear.
    /// </summary>
    public class ReportBreakdownTests
    {
        private ScoreService _score = null!;

        [SetUp]
        public void SetUp() => _score = new ScoreService();

        [Test]
        public void Ledger_RecordsOnlyNegativeDeltasWithTheirCodeAndTask()
        {
            _score.AddPoints(150, "Task completed", "equip_helmet");
            _score.SubtractPoints(30, ViolationCodes.WrongPpeSelected, "equip_boots");
            _score.SubtractPoints(50, ViolationCodes.HazardMisclassified, "report_mesh");

            Assert.AreEqual(2, _score.Ledger.Charges.Count);
            Assert.AreEqual(80, _score.Ledger.TotalCharged);
            var boots = _score.Ledger.ChargesFor("equip_boots").Single();
            Assert.AreEqual(ViolationCodes.WrongPpeSelected, boots.Code);
            Assert.AreEqual(30, boots.Amount);
        }

        [Test]
        public void Ledger_ResetClearsTheChargesAndIsNotCountedAsOne()
        {
            _score.AddPoints(100, "Task completed", "t");
            _score.SubtractPoints(30, ViolationCodes.WrongPpeSelected, "t");

            _score.ResetSession();

            Assert.AreEqual(0, _score.Ledger.Charges.Count, "The reset delta undoes the old total; it is not a penalty.");
        }

        [Test]
        public void Ledger_ResetOfANegativeScoreDoesNotInventACharge()
        {
            _score.SubtractPoints(30, ViolationCodes.WrongPpeSelected, "t");

            _score.ResetSession();
            _score.AddPoints(10, "Task completed", "t");

            Assert.AreEqual(0, _score.Ledger.Charges.Count);
        }

        [Test]
        public void ChargesOutside_ReturnsTaskLessAndUnreportedChargesOnly()
        {
            _score.SubtractPoints(30, ViolationCodes.WrongPpeSelected, "equip_boots");
            _score.SubtractPoints(30, ViolationCodes.WrongPpeSelected, "");
            _score.SubtractPoints(20, ViolationCodes.GatePenalty, "ghost_task");

            var outside = _score.Ledger.ChargesOutside(new HashSet<string> { "equip_boots" });

            Assert.AreEqual(new[] { "", "ghost_task" }, outside.Select(c => c.TaskId).ToArray());
        }

        [Test]
        public void EarnedMinusListedCharges_EqualsTheFinalScore()
        {
            var bus = new FakeEventBus();
            var builder = new FakeTaskBuilder();
            var scoreEngine = new ScoreRuleEngineCore(bus, _score, config: ScoringConfig.Default);
            scoreEngine.Subscribe();
            var safetyEngine = new SafetyRuleEngineCore(bus, () => SessionMode.Guided);
            safetyEngine.Subscribe();

            var boots = builder.Task("equip_boots", string.Empty, PPEType.Boots);
            boots.riskLevel = RiskLevel.Moderate;
            var mesh = builder.Task("report_mesh", "flag_safety_net");
            mesh.riskLevel = RiskLevel.Moderate;
            var group = builder.Group("g", TaskExecutionModeShared.FreeOrder, boots, mesh);

            try
            {
                bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));
                bus.Publish(new PPEStateChangedEventArgs(PPEType.Boots, true));
                bus.Publish(new ActionAttemptedEvent("flag_safety_net", sourceId: "rip"));

                IncidentalChargePolicy.ChargeWrongPpeSelected(
                    bus, _score, ScoringConfig.Default, "Chinelo", "FlipFlops", true, "equip_boots");
                HazardClassificationPolicy.ChargeMisclassification(
                    bus, _score, ScoringConfig.Default, mesh, group, "rip");

                int earned = 0;
                foreach (var task in new ISafetyTask[] { boots, mesh }) earned += ScoringConfig.Default.PointsFor(task.riskLevel);

                Assert.AreEqual(2, _score.Ledger.Charges.Count);
                Assert.AreEqual(_score.CurrentScore, earned - _score.Ledger.TotalCharged);
            }
            finally
            {
                scoreEngine.Dispose();
                safetyEngine.Dispose();
            }
        }

        [Test]
        public void Labels_ReusePortugueseDashboardWordingAndFallBackToTheCode()
        {
            Assert.AreEqual("Equipamento inadequado selecionado", ScoreChargeLabels.For(ViolationCodes.WrongPpeSelected));
            Assert.AreEqual("Classificação incorreta da irregularidade", ScoreChargeLabels.For(ViolationCodes.HazardMisclassified));
            Assert.AreEqual("Inspeção não aprovada", ScoreChargeLabels.For(ViolationCodes.GatePenalty));
            Assert.AreEqual("UNKNOWN_CODE", ScoreChargeLabels.For("UNKNOWN_CODE"));
            Assert.AreEqual("Equipamento inadequado selecionado: -30 pts",
                ScoreChargeLabels.FormatLine(new ScoreCharge(ViolationCodes.WrongPpeSelected, 30, "t")));
        }

        private static RuntimeSafetyTask MeshTask(string reportedOptionId)
        {
            var task = new FakeTaskBuilder().Task("report_mesh", "flag_safety_net");
            task.failureAdvice = "A tela estava rasgada.";
            task.ReportOptions.Add(new FakeTaskBuilder.FakeReportOption { Id = "torn_mesh", Label = "Tela rasgada", Correct = true });
            task.ReportOptions.Add(new FakeTaskBuilder.FakeReportOption { Id = "no_irregularity", Label = "Sem irregularidade" });
            return new RuntimeSafetyTask(task)
            {
                State = TaskState.CompletedSuccess,
                ReportedOptionId = reportedOptionId,
            };
        }

        [Test]
        public void HasMisclassifiedReport_TrueOnlyForAPickedIncorrectOption()
        {
            Assert.IsTrue(MeshTask("no_irregularity").HasMisclassifiedReport);
            Assert.IsFalse(MeshTask("torn_mesh").HasMisclassifiedReport);
            Assert.IsFalse(MeshTask(string.Empty).HasMisclassifiedReport, "No pick is not a misclassification.");
            Assert.IsFalse(MeshTask("not_an_option").HasMisclassifiedReport);
        }

        [Test]
        public void HasMisclassifiedReport_FalseForATaskWithoutOptions()
        {
            var plain = new RuntimeSafetyTask(new FakeTaskBuilder().Task("t", "a")) { ReportedOptionId = "x" };

            Assert.IsFalse(plain.HasMisclassifiedReport);
        }

        [Test]
        public void CorrectionAdvice_NamesTheCorrectOptionThenTheFailureAdvice()
        {
            var advice = HazardClassificationPolicy.BuildCorrectionAdvice(MeshTask("no_irregularity").TaskData);

            StringAssert.StartsWith(HazardClassificationPolicy.CorrectClassificationPrefix + "Tela rasgada.", advice);
            StringAssert.EndsWith("A tela estava rasgada.", advice);
        }
    }
}
