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

        // ── CloseCurrentGroup ──────────────────────────────────────────────────────

        [Test]
        public void CloseCurrentGroup_ClosesPendingAsNotPerformed_AndCompletesGroup()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess }, TaskPhase.Completed));

            var closed = core.CloseCurrentGroup();

            Assert.AreEqual(1, closed.Count);
            Assert.AreEqual(TaskState.NotPerformed, closed[0].State);
            Assert.AreEqual("t2", closed[0].taskName);

            var completed = _groupEvents.FindAll(e => e.Phase == TaskGroupPhase.Completed);
            Assert.AreEqual(1, completed.Count);

            var notPerformedViolations = _violations.FindAll(v => v.ViolationCode == "TASK_NOT_PERFORMED");
            Assert.AreEqual(1, notPerformedViolations.Count);
            Assert.AreEqual(t2.id, notPerformedViolations[0].TaskId);

            core.Dispose();
        }

        [Test]
        public void CloseCurrentGroup_LastGroup_EndsSession()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            var closed = core.CloseCurrentGroup();

            Assert.AreEqual(2, closed.Count);
            Assert.AreEqual(1, _sessionCompletions.Count);
            Assert.AreEqual(0, _sessionCompletions[0].tasksCompleted);
            Assert.AreEqual(2, _sessionCompletions[0].totalTasks);
            Assert.AreEqual(1, _sessionEnded.Count);
            Assert.IsTrue(core.LastSessionSummary.HasValue);

            core.Dispose();
        }

        [Test]
        public void CloseCurrentGroup_NoActiveGroup_IsNoOp()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            // StartSession() intentionally not called — no active group yet.

            var closed = core.CloseCurrentGroup();

            Assert.IsEmpty(closed);
            Assert.IsEmpty(_groupEvents);
            Assert.IsEmpty(_taskEvents);
            Assert.IsEmpty(_violations);
            Assert.IsEmpty(_sessionCompletions);

            core.Dispose();
        }

        [Test]
        public void CloseCurrentGroup_WithPendingTasks_StartsTheNextGroup()
        {
            // The Evaluation phase-advance gate closes its group while a task is still
            // open (the participant skipped a PPE). The session must move on to the next
            // group, exactly as it does when the group completes on its own. This is the
            // regression that stranded a participant on a scaffold whose inspection group
            // never started, and it depends on CheckGroupCompletion treating NotPerformed
            // as terminal.
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var groupA = _tasks.Group("gA", TaskExecutionModeShared.FreeOrder, t1, t2);

            var t3 = _tasks.Task("t3", "action_c");
            var groupB = _tasks.Group("gB", TaskExecutionModeShared.FreeOrder, t3);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { groupA, groupB });
            core.Subscribe();
            core.StartSession();

            core.CloseCurrentGroup();

            var started = _groupEvents.FindAll(e => e.Phase == TaskGroupPhase.Started);
            Assert.AreEqual(2, started.Count, "groupB should have started after groupA was closed.");
            Assert.AreEqual("gB", started[1].Group!.groupName);
            Assert.AreEqual("gB", core.GetCurrentGroup()!.groupName);

            core.Dispose();
        }

        [Test]
        public void EndSession_SummaryCarriesPerTaskOutcomesWithTheirRisk()
        {
            // The session log's per-task block is built from this payload. It has to carry the
            // risk grading in force during the run so a log stays readable after the scenario's
            // matrix is regraded.
            var t1 = _tasks.Task("t1", "action_a");
            t1.risk = RiskAssessment.FromGrades(4, 3);
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess }, TaskPhase.Completed));
            core.CloseCurrentGroup();

            var outcomes = _sessionCompletions[0].taskOutcomes;
            Assert.AreEqual(2, outcomes.Length);

            Assert.AreEqual("t1", outcomes[0].TaskId);
            Assert.AreEqual("g1", outcomes[0].GroupId);
            Assert.AreEqual(TaskState.CompletedSuccess, outcomes[0].State);
            Assert.AreEqual(4, outcomes[0].Risk.Severity);
            Assert.AreEqual(3, outcomes[0].Risk.Probability);

            Assert.AreEqual("t2", outcomes[1].TaskId);
            Assert.AreEqual(TaskState.NotPerformed, outcomes[1].State);

            core.Dispose();
        }

        // ── GetCompletionOrderDeviations ─────────────────────────────────────────

        [Test]
        public void GetCompletionOrderDeviations_InOrder_ReturnsEmpty()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var t3 = _tasks.Task("t3", "action_c");
            var t4 = _tasks.Task("t4", "action_d");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2, t3, t4);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            // t4 is left NotStarted so the group never completes and stays "current"
            // for the GetCompletionOrderDeviations() call below.
            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess, CompletionTime = 1f }, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t2, new RuntimeSafetyTask(t2) { State = TaskState.CompletedSuccess, CompletionTime = 2f }, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t3, new RuntimeSafetyTask(t3) { State = TaskState.CompletedSuccess, CompletionTime = 3f }, TaskPhase.Completed));

            var deviations = core.GetCompletionOrderDeviations();

            Assert.IsEmpty(deviations);

            core.Dispose();
        }

        [Test]
        public void GetCompletionOrderDeviations_OutOfOrder_NamesTheEarlyTask()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var t3 = _tasks.Task("t3", "action_c");
            var t4 = _tasks.Task("t4", "action_d");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2, t3, t4);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            // Group order is t1, t2, t3, t4. t3 completes chronologically BEFORE t2
            // (1s vs 2s), but the algorithm walks tasks in authored group order and
            // compares each one's CompletionTime against the immediately preceding
            // group-order task's CompletionTime — so it is t3 (index 2, compared
            // against t2 at index 1) whose timestamp (1.5) is lower than its
            // predecessor's (2), not t2 itself. t4 stays pending to keep the group
            // "current".
            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess, CompletionTime = 1f }, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t2, new RuntimeSafetyTask(t2) { State = TaskState.CompletedSuccess, CompletionTime = 2f }, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t3, new RuntimeSafetyTask(t3) { State = TaskState.CompletedSuccess, CompletionTime = 1.5f }, TaskPhase.Completed));

            var deviations = core.GetCompletionOrderDeviations();

            Assert.AreEqual(1, deviations.Count);
            Assert.AreEqual("t3", deviations[0]);

            core.Dispose();
        }

        [Test]
        public void GetCompletionOrderDeviations_IgnoresPendingAndNotPerformed()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var t3 = _tasks.Task("t3", "action_c");
            var t4 = _tasks.Task("t4", "action_d");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2, t3, t4);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group });
            core.Subscribe();
            core.StartSession();

            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess, CompletionTime = 1f }, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t3, new RuntimeSafetyTask(t3) { State = TaskState.CompletedSuccess, CompletionTime = 2f }, TaskPhase.Completed));

            // t2 is closed directly (not via CloseCurrentGroup, which would also close t4
            // and complete the group) — a task that never completed must be skipped rather
            // than treated as an out-of-order completion.
            var t2Runtime = core.GetSessionTasks()[1];
            Assert.AreEqual("t2", t2Runtime.taskName);
            t2Runtime.State = TaskState.NotPerformed;
            t2Runtime.CompletionTime = 0.5f;

            // t4 stays NotStarted (pending) so the group remains "current".
            var deviations = core.GetCompletionOrderDeviations();

            Assert.IsEmpty(deviations);

            core.Dispose();
        }
    }
}
