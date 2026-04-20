using System.Collections.Generic;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Tasks;
using SafetyProto.Runtime.Task;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class TaskManagerCoreTests
    {
        private FakeEventBus _bus;
        private FakeTaskBuilder _tasks;
        private FakeScoreService _score;
        private List<TaskGroupEventArgs> _groupEvents;
        private List<TaskEventArgs> _taskEvents;
        private List<SessionCompletedEventArgs> _sessionCompletions;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _tasks = new FakeTaskBuilder();
            _score = new FakeScoreService();
            _groupEvents = new List<TaskGroupEventArgs>();
            _taskEvents = new List<TaskEventArgs>();
            _sessionCompletions = new List<SessionCompletedEventArgs>();

            _bus.Subscribe<TaskGroupEventArgs>(args => _groupEvents.Add(args));
            _bus.Subscribe<TaskEventArgs>(args => _taskEvents.Add(args));
            _bus.Subscribe<SessionCompletedEventArgs>(args => _sessionCompletions.Add(args));
        }

        [Test]
        public void StartSession_WithOneGroupOneTask_PublishesGroupStartedThenTaskStarted()
        {
            var task = _tasks.Task("t1", "action_a");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, task);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            Assert.AreEqual(1, _groupEvents.Count);
            Assert.AreEqual(TaskGroupPhase.Started, _groupEvents[0].Phase);
            Assert.AreEqual("g1", _groupEvents[0].Group.groupName);

            Assert.AreEqual(1, _taskEvents.Count);
            Assert.AreEqual(TaskPhase.Started, _taskEvents[0].Phase);
            Assert.AreEqual("t1", _taskEvents[0].Task.taskName);

            core.Dispose();
        }

        [Test]
        public void TaskCompleted_AdvancesToNextTaskInSameGroup()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            Assert.AreEqual(1, _taskEvents.Count);

            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess }, TaskPhase.Completed));

            var started = _taskEvents.FindAll(e => e.Phase == TaskPhase.Started);
            Assert.AreEqual(2, started.Count);
            Assert.AreEqual("t2", started[1].Task.taskName);

            core.Dispose();
        }

        [Test]
        public void AllTasksCompleted_PublishesGroupCompletedAndSessionCompleted()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess }, TaskPhase.Completed));

            var completed = _groupEvents.FindAll(e => e.Phase == TaskGroupPhase.Completed);
            Assert.AreEqual(1, completed.Count);

            Assert.AreEqual(1, _sessionCompletions.Count);
            Assert.AreEqual(1, _sessionCompletions[0].totalTasks);
            Assert.AreEqual(1, _sessionCompletions[0].tasksCompleted);

            core.Dispose();
        }

        [Test]
        public void TaskTimeout_MarksFailedAndAdvances()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            _bus.Publish(new TaskEventArgs(t1, null, TaskPhase.Timeout));

            var started = _taskEvents.FindAll(e => e.Phase == TaskPhase.Started);
            Assert.AreEqual(2, started.Count, "Second task should have started after first timed out.");
            Assert.AreEqual("t2", started[1].Task.taskName);

            core.Dispose();
        }

        [Test]
        public void GroupDependency_UnmetGroupIsSkipped()
        {
            var tA = _tasks.Task("tA", "action_a");
            var groupA = _tasks.Group("groupA", TaskExecutionModeShared.Sequential, tA);

            var tB = _tasks.Task("tB", "action_b");
            var groupB = _tasks.Group("groupB", TaskExecutionModeShared.Sequential, tB);
            var phantomGroup = _tasks.Group("phantom", TaskExecutionModeShared.Sequential);
            groupB.requiredGroups = new List<ITaskGroup> { phantomGroup };

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { groupA, groupB });
            core.Subscribe();
            core.StartSession();

            _bus.Publish(new TaskEventArgs(tA, new RuntimeSafetyTask(tA) { State = TaskState.CompletedSuccess }, TaskPhase.Completed));

            Assert.AreEqual(1, _sessionCompletions.Count);
            var groupsStarted = _groupEvents.FindAll(e => e.Phase == TaskGroupPhase.Started);
            Assert.AreEqual(1, groupsStarted.Count, "Only groupA should have started.");

            core.Dispose();
        }

        [Test]
        public void FindPendingTaskByActionId_SequentialMode_ReturnsCurrentTaskIfMatches()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            var found = core.FindPendingTaskByActionId("action_a");
            Assert.IsNotNull(found);
            Assert.AreEqual("t1", found.taskName);

            var notFound = core.FindPendingTaskByActionId("action_nonexistent");
            Assert.IsNull(notFound);

            core.Dispose();
        }

        private sealed class FakeScoreService : IScoreService
        {
            public int CurrentScore { get; private set; }
            public void AddPoints(int amount, string reason = null) => CurrentScore += amount;
            public void SubtractPoints(int amount, string reason = null) => CurrentScore -= amount;
            public void Reset() => CurrentScore = 0;
#pragma warning disable CS0067
            public event System.Action<int, int, string> ScoreChanged;
#pragma warning restore CS0067
        }
    }
}
