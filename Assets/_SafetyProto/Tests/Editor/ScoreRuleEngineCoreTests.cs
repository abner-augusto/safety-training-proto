using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scoring;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class ScoreRuleEngineCoreTests
    {
        private FakeEventBus _bus = null!;
        private ScoreService _score = null!;
        private FakeTaskBuilder _builder = null!;
        private ScoreRuleEngineCore _core = null!;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _score = new ScoreService();
            _builder = new FakeTaskBuilder();
        }

        [TearDown]
        public void TearDown()
        {
            _core?.Dispose();
        }

        [Test]
        public void TaskCompleted_Safe_Moderate_AddsFullTierPoints()
        {
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Moderate;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed));

            Assert.AreEqual(150, _score.CurrentScore);
        }

        [Test]
        public void TaskCompleted_Unsafe_Moderate_EarnsHalfTierPoints()
        {
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Moderate;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            var runtimeTask = new RuntimeSafetyTask(task)
            {
                State = TaskState.CompletedSuccessButUnsafe
            };

            _bus.Publish(new TaskEventArgs(task, runtimeTask, TaskPhase.Completed));

            Assert.AreEqual(75, _score.CurrentScore, "150 x moderateUnsafeFactor(0.5) = 75");
        }

        [Test]
        public void TaskCompleted_Unsafe_Minor_EarnsSeventyPercentOfTierPoints()
        {
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Minor;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            var runtimeTask = new RuntimeSafetyTask(task)
            {
                State = TaskState.CompletedSuccessButUnsafe
            };

            _bus.Publish(new TaskEventArgs(task, runtimeTask, TaskPhase.Completed));

            Assert.AreEqual(70, _score.CurrentScore, "100 x minorUnsafeFactor(0.7) = 70");
        }

        [Test]
        public void TaskCompleted_Unsafe_Critical_EarnsZero_DoesNotThrow_ScoreUnchanged()
        {
            // Plan-009 regression, re-expressed: AddPoints throws on amount <= 0, so an
            // unsafe critical completion (which earns 0 under the default config) must
            // never call it. Use the real ScoreService (not a fake) so the guard in
            // ScoreRuleEngineCore.ApplyTaskCompletedScoring is actually exercised.
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Critical;

            IScoreService score = new ScoreService();
            _core = new ScoreRuleEngineCore(_bus, score, config: ScoringConfig.Default);
            _core.Subscribe();

            var runtimeTask = new RuntimeSafetyTask(task)
            {
                State = TaskState.CompletedSuccessButUnsafe
            };

            bool scoreChangedRaised = false;
            score.ScoreChanged += (_, _, _, _) => scoreChangedRaised = true;

            Assert.DoesNotThrow(() =>
                _bus.Publish(new TaskEventArgs(task, runtimeTask, TaskPhase.Completed)));

            Assert.AreEqual(0, score.CurrentScore);
            Assert.IsFalse(scoreChangedRaised, "Zero-point unsafe completion must not raise ScoreChanged");
        }

        [Test]
        public void UnsafeEarn_RoundsToNearestInt()
        {
            var config = new ScoringConfig { ModeratePoints = 145, ModerateUnsafeFactor = 0.5 };
            Assert.AreEqual(72, config.UnsafeEarnFor(TaskSeverity.Moderate)); // 72.5 -> Math.Round banker's rounding -> 72
        }

        [Test]
        public void TaskCompleted_NullRuntimeTask_CompliantDefaultsToSuccess()
        {
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Moderate;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            // TaskEventArgs ctor defaults WasPpeCompliant = true -> treated as a clean completion.
            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed));

            Assert.AreEqual(150, _score.CurrentScore);
        }

        [Test]
        public void TaskCompleted_NullRuntimeTask_NotCompliant_EarnsUnsafeAmount()
        {
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Moderate;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            // Non-compliant completion with no RuntimeTask instance: unsafe state is conveyed via
            // WasPpeCompliant=false (the path SafetyRuleEngineCore uses). The unsafe factor must apply.
            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed) { WasPpeCompliant = false });

            Assert.AreEqual(75, _score.CurrentScore, "150 x moderateUnsafeFactor(0.5) = 75");
        }

        [Test]
        public void TaskCompleted_FailureState_NoPointsAdded()
        {
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Moderate;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            var runtimeTask = new RuntimeSafetyTask(task)
            {
                State = TaskState.CompletedFailure
            };

            _bus.Publish(new TaskEventArgs(task, runtimeTask, TaskPhase.Completed));

            Assert.AreEqual(0, _score.CurrentScore);
        }

        [Test]
        public void TaskTimeout_Minor_SubtractsMinorBasePenalty()
        {
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Minor;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Timeout));

            Assert.AreEqual(-30, _score.CurrentScore);
        }

        [Test]
        public void TaskTimeout_Critical_SubtractsCriticalBasePenalty()
        {
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Critical;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Timeout));

            Assert.AreEqual(-100, _score.CurrentScore);
        }

        [Test]
        public void TaskCompleted_NullTask_NoChange()
        {
            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(null!, null, TaskPhase.Completed));

            Assert.AreEqual(0, _score.CurrentScore);
        }

        [Test]
        public void TaskTimeout_NullTask_NoChange()
        {
            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(null!, null, TaskPhase.Timeout));

            Assert.AreEqual(0, _score.CurrentScore);
        }

        [Test]
        public void Dispose_UnsubscribesFromBus()
        {
            var task = _builder.Task("t1", "action_a");
            task.severity = TaskSeverity.Moderate;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();
            _core.Dispose();

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed));

            Assert.AreEqual(0, _score.CurrentScore);
        }

        [Test]
        public void MultipleTasks_AccumulateScore()
        {
            var t1 = _builder.Task("t1", "action_a");
            t1.severity = TaskSeverity.Moderate;
            var t2 = _builder.Task("t2", "action_b");
            t2.severity = TaskSeverity.Minor;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(t1, null, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t2, null, TaskPhase.Completed));

            Assert.AreEqual(250, _score.CurrentScore, "150 (moderate) + 100 (minor) = 250");
        }

        [Test]
        public void UnsafeThenSafeTask_CorrectRunningScore()
        {
            var t1 = _builder.Task("t1", "action_a");
            t1.severity = TaskSeverity.Moderate;

            var t2 = _builder.Task("t2", "action_b");
            t2.severity = TaskSeverity.Minor;

            _core = new ScoreRuleEngineCore(_bus, _score, config: ScoringConfig.Default);
            _core.Subscribe();

            var unsafeTask = new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccessButUnsafe };
            _bus.Publish(new TaskEventArgs(t1, unsafeTask, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t2, null, TaskPhase.Completed));

            Assert.AreEqual(175, _score.CurrentScore, "75 (unsafe moderate) + 100 (safe minor) = 175");
        }
    }
}
