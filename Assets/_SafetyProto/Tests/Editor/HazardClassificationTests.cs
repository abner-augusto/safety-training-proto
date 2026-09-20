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
    /// Covers plan 044 Block C: choosing the wrong option on a hazard-classification report
    /// still completes the task but charges the tier's base penalty; choosing the right one
    /// charges nothing; missing PPE and a wrong classification compound.
    ///
    /// <see cref="SafetyIssueReporter"/> is a MonoBehaviour driven by <c>OnEnable</c> wiring
    /// that EditMode never runs, so these tests exercise the same pure pieces it calls —
    /// <see cref="SafetyRuleEngineCore"/> for task completion/PPE compliance,
    /// <see cref="ScoreRuleEngineCore"/> for the points a completion earns, and
    /// <see cref="HazardClassificationPolicy"/> for the misclassification charge — wired to
    /// the same <see cref="FakeEventBus"/> and a real <see cref="ScoreService"/>, exactly as
    /// they are wired at runtime.
    /// </summary>
    public class HazardClassificationTests
    {
        private FakeEventBus _bus = null!;
        private ScoreService _score = null!;
        private FakeTaskBuilder _tasks = null!;
        private SafetyRuleEngineCore _safetyEngine = null!;
        private ScoreRuleEngineCore _scoreEngine = null!;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _score = new ScoreService();
            _tasks = new FakeTaskBuilder();

            _safetyEngine = new SafetyRuleEngineCore(_bus);
            _safetyEngine.Subscribe();

            _scoreEngine = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _scoreEngine.Subscribe();
        }

        [TearDown]
        public void TearDown()
        {
            _safetyEngine.Dispose();
            _scoreEngine.Dispose();
            SessionModeState.Reset();
        }

        private FakeTaskBuilder.FakeSafetyTask ReportTask(RiskLevel level = RiskLevel.Moderate)
        {
            var task = _tasks.Task("Comunicar Irregularidade na Tela Fachadeira", "flag_safety_net",
                PPEType.Harness, PPEType.Helmet);
            task.riskLevel = level;
            return task;
        }

        /// <summary>Simulates what SafetyIssueReporter.PublishReport does for a wrong pick:
        /// publish the action attempt (completing the task through the normal path), then
        /// charge the misclassification exactly as the reporter's helper would.</summary>
        private void PublishWrongClassification(ISafetyTask task, ITaskGroup group)
        {
            _bus.Publish(new ActionAttemptedEvent("flag_safety_net", sourceId: "MeshRip_Location"));
            HazardClassificationPolicy.ChargeMisclassification(
                _bus, _score, ScoringConfig.Default, task, group, "MeshRip_Location");
        }

        [Test]
        public void WrongOption_TaskCompletesAndChargesTierPenalty()
        {
            var task = ReportTask();
            var group = _tasks.Group("Inspeção", TaskExecutionModeShared.FreeOrder, task);
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Harness, true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, true));

            PublishWrongClassification(task, group);

            Assert.AreEqual(100, _score.CurrentScore,
                "150 (moderate) earned on completion, minus 50 (moderate penalty) = 100.");
        }

        [Test]
        public void WrongOption_RaisesHazardMisclassifiedViolation()
        {
            var task = ReportTask();
            var group = _tasks.Group("Inspeção", TaskExecutionModeShared.FreeOrder, task);
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Harness, true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, true));

            SafetyViolationEventArgs? violation = null;
            _bus.Subscribe<SafetyViolationEventArgs>(args =>
            {
                if (args.ViolationCode == ViolationCodes.HazardMisclassified) violation = args;
            });

            PublishWrongClassification(task, group);

            Assert.IsNotNull(violation, "Choosing a wrong option must raise HAZARD_MISCLASSIFIED.");
            Assert.AreEqual(task.id, violation!.Value.TaskId);
        }

        [Test]
        public void RightOption_NoChargeIsApplied()
        {
            var task = ReportTask();
            var group = _tasks.Group("Inspeção", TaskExecutionModeShared.FreeOrder, task);
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Harness, true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, true));

            // A correct pick never calls HazardClassificationPolicy at all — nothing to charge.
            _bus.Publish(new ActionAttemptedEvent("flag_safety_net", sourceId: "MeshRip_Location"));

            Assert.AreEqual(150, _score.CurrentScore, "Full moderate points, no penalty.");
        }

        [Test]
        public void CompoundCase_MissingPpeAndWrongOption_NetsQuarterPoints()
        {
            // Plan 044: "missing PPE (x0.5 -> 75) plus misclassification (-50) = 25".
            var task = ReportTask();
            var group = _tasks.Group("Inspeção", TaskExecutionModeShared.FreeOrder, task);
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started));
            // Deliberately no PPEStateChanged events — Harness/Helmet stay unworn.

            PublishWrongClassification(task, group);

            Assert.AreEqual(25, _score.CurrentScore,
                "75 (unsafe moderate completion) - 50 (misclassification penalty) = 25.");
        }

        [Test]
        public void ChargeMisclassification_ZeroPenaltyTier_DoesNotThrowOrChangeScore()
        {
            // Trivial tier's penalty (20) is still > 0 under defaults, so force a zero-penalty
            // config to exercise the SubtractPoints(amount<=0) guard directly.
            var config = new ScoringConfig();
            config.Levels["moderate"] = new RiskLevelScoring(points: 150, penalty: 0, unsafeFactor: 0.5);

            var task = ReportTask();
            var group = _tasks.Group("Inspeção", TaskExecutionModeShared.FreeOrder, task);

            Assert.DoesNotThrow(() =>
                HazardClassificationPolicy.ChargeMisclassification(_bus, _score, config, task, group, "reporter"));
            Assert.AreEqual(0, _score.CurrentScore);
        }
    }
}
