using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Sessions;
using SafetyProto.Domain.Tasks;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// An out-of-order completion is a neutral note on the finish report: it is recorded on the
    /// task when the deviation is registered, and it never costs points or removes the clean-run card.
    /// </summary>
    public class OrderNoteTests
    {
        private const string OrderNote = " Alguns EPIs foram colocados fora da ordem recomendada. Isso não gerou desconto.";
        private const string OrderLine = "Feita fora da ordem recomendada (sem desconto).";

        private FakeEventBus _bus = null!;
        private ScoreService _score = null!;
        private TaskManagerCore _core = null!;
        private FakeTaskBuilder.FakeSafetyTask _t2 = null!;
        private FakeTaskBuilder.FakeSafetyTask _t3 = null!;

        [SetUp]
        public void SetUp()
        {
            _bus = new FakeEventBus();
            _score = new ScoreService();
            var builder = new FakeTaskBuilder();
            var t1 = builder.Task("t1", "action_a");
            _t2 = builder.Task("t2", "action_b");
            _t3 = builder.Task("t3", "action_c");
            var group = builder.Group("g1", TaskExecutionModeShared.FreeOrder, t1, _t2, _t3);

            _core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            _core.Subscribe();
            _core.StartSession();

            // t3 completes before t2, which precedes it in the authored order.
            Complete(t1, 1f);
            Complete(_t2, 2f);
            Complete(_t3, 1.5f);
        }

        [TearDown]
        public void TearDown() => _core.Dispose();

        private void Complete(ISafetyTask task, float time) =>
            _bus.Publish(new TaskEventArgs(task,
                new RuntimeSafetyTask(task) { State = TaskState.CompletedSuccess, CompletionTime = time },
                TaskPhase.Completed));

        private RuntimeSafetyTask Runtime(ISafetyTask task) =>
            _core.GetSessionTasks().Single(t => ReferenceEquals(t.TaskData, task));

        [Test]
        public void Querying_DoesNotMarkAnyTask()
        {
            _core.GetCompletionOrderDeviations("g1");

            Assert.IsFalse(_core.GetSessionTasks().Any(t => t.CompletedOutOfOrder));
        }

        [Test]
        public void Registering_MarksOnlyTheDeviatingTaskAndLeavesTheScore()
        {
            int scoreBefore = _score.CurrentScore;

            var names = _core.MarkCompletionOrderDeviations("g1");

            CollectionAssert.AreEqual(new[] { "t3" }, names);
            Assert.IsTrue(Runtime(_t3).CompletedOutOfOrder);
            Assert.IsFalse(Runtime(_t2).CompletedOutOfOrder);
            Assert.AreEqual(scoreBefore, _score.CurrentScore);
            Assert.AreEqual(TaskState.CompletedSuccess, Runtime(_t3).State, "The state is untouched.");
        }

        [Test]
        public void ResetSession_ClearsTheMark()
        {
            _core.MarkCompletionOrderDeviations("g1");

            _core.ResetSession();

            Assert.IsFalse(_core.GetSessionTasks().Any(t => t.CompletedOutOfOrder));
        }

        [Test]
        public void RowDetail_AppendsTheOrderLineOnlyWhenMarked_AndAddsNoPoints()
        {
            var noCharges = new List<ScoreCharge>();

            Assert.AreEqual(OrderLine, ReportComposition.BuildRowDetail("", noCharges, true, OrderLine));
            Assert.AreEqual("", ReportComposition.BuildRowDetail("", noCharges, false, OrderLine));
            Assert.AreEqual("Conselho\n" + OrderLine, ReportComposition.BuildRowDetail("Conselho", noCharges, true, OrderLine));
            StringAssert.DoesNotContain("-", ReportComposition.BuildRowDetail("", noCharges, true, OrderLine).Replace("fora da ordem", ""));
        }

        [Test]
        public void RowDetail_ListsChargesBeforeTheOrderLine()
        {
            var charges = new List<ScoreCharge> { new ScoreCharge(ViolationCodes.WrongPpeSelected, 30, "t") };

            var detail = ReportComposition.BuildRowDetail("", charges, true, OrderLine).Split('\n');

            Assert.AreEqual("Equipamento inadequado selecionado: -30 pts", detail[0]);
            Assert.AreEqual(OrderLine, detail[1]);
        }

        [TestCase(true, 0, PrimaryCardKind.Issue)]
        [TestCase(true, 90, PrimaryCardKind.Issue)]
        [TestCase(false, 90, PrimaryCardKind.ChargesOnly)]
        [TestCase(false, 0, PrimaryCardKind.Safe)]
        public void PrimaryCard_IssueBeatsChargesBeatsSafe(bool hasIssue, int charged, PrimaryCardKind expected)
        {
            Assert.AreEqual(expected, ReportComposition.DecidePrimaryCard(hasIssue, charged));
        }

        [TestCase("corpo", true, "corpo" + OrderNote)]
        [TestCase("corpo", false, "corpo")]
        public void OrderNote_IsAppendedToWhicheverBodyIsShown(string body, bool anyOutOfOrder, string expected)
        {
            Assert.AreEqual(expected, ReportComposition.AppendOrderNote(body, anyOutOfOrder, OrderNote));
        }

        [Test]
        public void ChargesBody_NamesTheLedgerTotal()
        {
            _score.SubtractPoints(30, ViolationCodes.WrongPpeSelected, "a");
            _score.SubtractPoints(60, ViolationCodes.HazardMisclassified, "b");

            var body = ReportComposition.FormatChargesBody("perdeu {0} pontos", _score.Ledger.TotalCharged);

            Assert.AreEqual("perdeu 90 pontos", body);
        }

        [Test]
        public void CleanRunWithADeviation_StillShowsTheSafeCardWithTheOrderSentence()
        {
            _core.MarkCompletionOrderDeviations("g1");
            bool anyOutOfOrder = _core.GetSessionTasks().Any(t => t.CompletedOutOfOrder);

            var kind = ReportComposition.DecidePrimaryCard(false, _score.Ledger.TotalCharged);
            var body = ReportComposition.AppendOrderNote("Você concluiu todas as tarefas.", anyOutOfOrder, OrderNote);

            Assert.AreEqual(PrimaryCardKind.Safe, kind, "A deviation alone must not remove the clean-run card.");
            StringAssert.EndsWith(OrderNote, body);
        }
    }
}
