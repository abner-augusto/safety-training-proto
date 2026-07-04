using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scoring;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class ScoreRuleEngineCoreTests
    {
        private FakeEventBus _bus;
        private FakeScoreService _score;
        private FakeTaskBuilder _builder;
        private ScoreRuleEngineCore _core;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _score = new FakeScoreService();
            _builder = new FakeTaskBuilder();
        }

        [TearDown]
        public void TearDown()
        {
            _core?.Dispose();
        }

        [Test]
        public void TaskCompleted_AddsSuccessPoints()
        {
            var task = _builder.Task("t1", "action_a");
            task.successPoints = 100;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed));

            Assert.AreEqual(100, _score.CurrentScore);
        }

        [Test]
        public void TaskCompleted_Unsafe_SubtractsPpePenalty()
        {
            var task = _builder.Task("t1", "action_a");
            task.successPoints = 100;
            task.ppePenalty = 50;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            var runtimeTask = new RuntimeSafetyTask(task)
            {
                State = TaskState.CompletedSuccessButUnsafe
            };

            _bus.Publish(new TaskEventArgs(task, runtimeTask, TaskPhase.Completed));

            Assert.AreEqual(50, _score.CurrentScore);
        }

        [Test]
        public void TaskCompleted_Unsafe_WithZeroPpePenalty_OnlyAddsSuccessPoints()
        {
            var task = _builder.Task("t1", "action_a");
            task.successPoints = 100;
            task.ppePenalty = 0;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            var runtimeTask = new RuntimeSafetyTask(task)
            {
                State = TaskState.CompletedSuccessButUnsafe
            };

            _bus.Publish(new TaskEventArgs(task, runtimeTask, TaskPhase.Completed));

            Assert.AreEqual(100, _score.CurrentScore);
        }

        [Test]
        public void ZeroPointTask_Completed_DoesNotThrow_ScoreUnchanged()
        {
            var task = _builder.Task("t1", "action_a");
            task.successPoints = 0;

            IScoreService score = new ScoreService();
            _core = new ScoreRuleEngineCore(_bus, score);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed));

            Assert.AreEqual(0, score.CurrentScore);
        }

        [Test]
        public void ZeroPointTask_UnsafeCompletion_StillAppliesPpePenalty()
        {
            var task = _builder.Task("t1", "action_a");
            task.successPoints = 0;
            task.ppePenalty = 50;

            IScoreService score = new ScoreService();
            _core = new ScoreRuleEngineCore(_bus, score);
            _core.Subscribe();

            var runtimeTask = new RuntimeSafetyTask(task)
            {
                State = TaskState.CompletedSuccessButUnsafe
            };

            _bus.Publish(new TaskEventArgs(task, runtimeTask, TaskPhase.Completed));

            Assert.AreEqual(-50, score.CurrentScore);
        }

        [Test]
        public void TaskCompleted_NullRuntimeTask_CompliantDefaultsToSuccess()
        {
            var task = _builder.Task("t1", "action_a");
            task.successPoints = 100;
            task.ppePenalty = 50;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            // TaskEventArgs ctor defaults WasPpeCompliant = true → treated as a clean completion,
            // no penalty even though RuntimeTask is null.
            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed));

            Assert.AreEqual(100, _score.CurrentScore);
        }

        [Test]
        public void TaskCompleted_NullRuntimeTask_NotCompliant_AppliesPpePenalty()
        {
            var task = _builder.Task("t1", "action_a");
            task.successPoints = 100;
            task.ppePenalty = 50;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            // Non-compliant completion with no RuntimeTask instance: unsafe state is conveyed via
            // WasPpeCompliant=false (the path SafetyRuleEngineCore uses). The penalty must apply.
            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed) { WasPpeCompliant = false });

            Assert.AreEqual(50, _score.CurrentScore, "successPoints(100) - ppePenalty(50) = 50");
        }

        [Test]
        public void TaskCompleted_FailureState_NoPointsAdded()
        {
            var task = _builder.Task("t1", "action_a");
            task.successPoints = 100;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            var runtimeTask = new RuntimeSafetyTask(task)
            {
                State = TaskState.CompletedFailure
            };

            _bus.Publish(new TaskEventArgs(task, runtimeTask, TaskPhase.Completed));

            Assert.AreEqual(0, _score.CurrentScore);
        }

        [Test]
        public void TaskTimeout_SubtractsFailurePenalty()
        {
            var task = _builder.Task("t1", "action_a");
            task.failurePenalty = 30;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Timeout));

            Assert.AreEqual(-30, _score.CurrentScore);
        }

        [Test]
        public void TaskTimeout_ZeroFailurePenalty_NoChange()
        {
            var task = _builder.Task("t1", "action_a");
            task.failurePenalty = 0;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Timeout));

            Assert.AreEqual(0, _score.CurrentScore);
        }

        [Test]
        public void TaskCompleted_NullTask_NoChange()
        {
            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(null, null, TaskPhase.Completed));

            Assert.AreEqual(0, _score.CurrentScore);
        }

        [Test]
        public void TaskTimeout_NullTask_NoChange()
        {
            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(null, null, TaskPhase.Timeout));

            Assert.AreEqual(0, _score.CurrentScore);
        }

        [Test]
        public void Dispose_UnsubscribesFromBus()
        {
            var task = _builder.Task("t1", "action_a");
            task.successPoints = 100;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();
            _core.Dispose();

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed));

            Assert.AreEqual(0, _score.CurrentScore);
        }

        [Test]
        public void MultipleTasks_AccumulateScore()
        {
            var t1 = _builder.Task("t1", "action_a");
            t1.successPoints = 100;
            var t2 = _builder.Task("t2", "action_b");
            t2.successPoints = 50;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            _bus.Publish(new TaskEventArgs(t1, null, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t2, null, TaskPhase.Completed));

            Assert.AreEqual(150, _score.CurrentScore);
        }

        [Test]
        public void UnsafeThenSafeTask_CorrectRunningScore()
        {
            var t1 = _builder.Task("t1", "action_a");
            t1.successPoints = 100;
            t1.ppePenalty = 80;

            var t2 = _builder.Task("t2", "action_b");
            t2.successPoints = 100;
            t2.ppePenalty = 0;

            _core = new ScoreRuleEngineCore(_bus, _score);
            _core.Subscribe();

            var unsafeTask = new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccessButUnsafe };
            _bus.Publish(new TaskEventArgs(t1, unsafeTask, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t2, null, TaskPhase.Completed));

            Assert.AreEqual(120, _score.CurrentScore);
        }

        private sealed class FakeScoreService : IScoreService
        {
            public int CurrentScore { get; private set; }
            public void AddPoints(int amount, string reason = null, string taskId = null) => CurrentScore += amount;
            public void SubtractPoints(int amount, string reason = null, string taskId = null) => CurrentScore -= amount;
#pragma warning disable CS0067
            public event System.Action<int, int, string, string> ScoreChanged;
#pragma warning restore CS0067
        }
    }
}
