using System.Collections.Generic;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Tasks;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class TaskManagerCoreTests
    {
        private FakeEventBus _bus = null!;
        private FakeTaskBuilder _tasks = null!;
        private ScoreService _score = null!;
        private List<TaskGroupEventArgs> _groupEvents = null!;
        private List<TaskEventArgs> _taskEvents = null!;
        private List<SessionCompletedEventArgs> _sessionCompletions = null!;
        private List<SafetyViolationEventArgs> _violations = null!;
        private List<SessionEndedEventArgs> _sessionEnded = null!;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _tasks = new FakeTaskBuilder();
            _score = new ScoreService();
            _groupEvents = new List<TaskGroupEventArgs>();
            _taskEvents = new List<TaskEventArgs>();
            _sessionCompletions = new List<SessionCompletedEventArgs>();
            _violations = new List<SafetyViolationEventArgs>();
            _sessionEnded = new List<SessionEndedEventArgs>();

            _bus.Subscribe<TaskGroupEventArgs>(args => _groupEvents.Add(args));
            _bus.Subscribe<TaskEventArgs>(args => _taskEvents.Add(args));
            _bus.Subscribe<SessionCompletedEventArgs>(args => _sessionCompletions.Add(args));
            _bus.Subscribe<SafetyViolationEventArgs>(args => _violations.Add(args));
            _bus.Subscribe<SessionEndedEventArgs>(args => _sessionEnded.Add(args));
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
            Assert.AreEqual("g1", _groupEvents[0].Group!.groupName);

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
            Assert.AreEqual("t1", found!.taskName);

            var notFound = core.FindPendingTaskByActionId("action_nonexistent");
            Assert.IsNull(notFound);

            core.Dispose();
        }

        // ── MarkPendingTasksOmitted ────────────────────────────────────────────────

        [Test]
        public void MarkPendingTasksOmitted_ClosesPendingAsOmitted_AndCompletesGroup()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess }, TaskPhase.Completed));

            var omitted = core.MarkPendingTasksOmitted();

            Assert.AreEqual(1, omitted.Count);
            Assert.AreEqual(TaskState.Omitted, omitted[0].State);
            Assert.AreEqual("t2", omitted[0].taskName);

            var completed = _groupEvents.FindAll(e => e.Phase == TaskGroupPhase.Completed);
            Assert.AreEqual(1, completed.Count);

            var taskOmittedViolations = _violations.FindAll(v => v.ViolationCode == "TASK_OMITTED");
            Assert.AreEqual(1, taskOmittedViolations.Count);
            Assert.AreEqual(t2.id, taskOmittedViolations[0].TaskId);

            core.Dispose();
        }

        [Test]
        public void MarkPendingTasksOmitted_LastGroup_EndsSession()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            var omitted = core.MarkPendingTasksOmitted();

            Assert.AreEqual(2, omitted.Count);
            Assert.AreEqual(1, _sessionCompletions.Count);
            Assert.AreEqual(0, _sessionCompletions[0].tasksCompleted);
            Assert.AreEqual(2, _sessionCompletions[0].totalTasks);
            Assert.AreEqual(1, _sessionEnded.Count);
            Assert.IsTrue(core.LastSessionSummary.HasValue);

            core.Dispose();
        }

        [Test]
        public void MarkPendingTasksOmitted_NoActiveGroup_IsNoOp()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            // StartSession() intentionally not called — no active group yet.

            var omitted = core.MarkPendingTasksOmitted();

            Assert.IsEmpty(omitted);
            Assert.IsEmpty(_groupEvents);
            Assert.IsEmpty(_taskEvents);
            Assert.IsEmpty(_violations);
            Assert.IsEmpty(_sessionCompletions);

            core.Dispose();
        }
    }
}
