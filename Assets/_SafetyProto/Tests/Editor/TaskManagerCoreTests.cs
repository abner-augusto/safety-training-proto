using System.Collections.Generic;
using System.Linq;
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

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _tasks = new FakeTaskBuilder();
            _score = new ScoreService();
        }

        // Projected from the bus's chronological publish log; per-type lists cannot prove event order.
        private List<TaskGroupEventArgs> _groupEvents => Of<TaskGroupEventArgs>();
        private List<TaskEventArgs> _taskEvents => Of<TaskEventArgs>();
        private List<SessionCompletedEventArgs> _sessionCompletions => Of<SessionCompletedEventArgs>();
        private List<SafetyViolationEventArgs> _violations => Of<SafetyViolationEventArgs>();
        private List<SessionEndedEventArgs> _sessionEnded => Of<SessionEndedEventArgs>();

        private List<T> Of<T>() =>
            _bus.PublishedEvents.Where(e => e.payload is T).Select(e => (T)e.payload).ToList();

        private TaskManagerCore StartPpeSequence(TaskExecutionModeShared mode)
        {
            var group = _tasks.Group("ppe", mode,
                _tasks.Task("equip_boots", string.Empty, PPEType.Boots),
                _tasks.Task("equip_gloves", string.Empty, PPEType.GloveLeft, PPEType.GloveRight),
                _tasks.Task("equip_harness", string.Empty, PPEType.Harness));
            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
            core.Subscribe();
            core.StartSession();
            return core;
        }

        [Test]
        public void RecordPpeOutOfOrder_LaterStepPpe_PublishesOneViolationNamingBothSteps()
        {
            var core = StartPpeSequence(TaskExecutionModeShared.Sequential);
            int scoreBefore = _score.CurrentScore;

            Assert.IsTrue(core.RecordPpeOutOfOrder(PPEType.Harness));

            var v = _violations.Single(x => x.ViolationCode == ViolationCodes.PpeOutOfOrder);
            Assert.AreEqual("equip_boots", v.TaskId, "The violation is stamped with the step that was due.");
            StringAssert.Contains("equip_harness", v.Message);
            StringAssert.Contains("equip_boots", v.Message);
            Assert.AreEqual(scoreBefore, _score.CurrentScore, "Out-of-order PPE is a log signal, not a penalty.");
            Assert.IsEmpty(Of<ActionRefusedEventArgs>());
            core.Dispose();
        }

        [Test]
        public void RecordPpeOutOfOrder_SameTypeRetried_LogsOnlyOnce()
        {
            var core = StartPpeSequence(TaskExecutionModeShared.Sequential);

            core.RecordPpeOutOfOrder(PPEType.Harness);
            Assert.IsFalse(core.RecordPpeOutOfOrder(PPEType.Harness));
            core.RecordPpeOutOfOrder(PPEType.GloveLeft);

            Assert.AreEqual(2, _violations.Count(x => x.ViolationCode == ViolationCodes.PpeOutOfOrder),
                "One entry per PPE type per group, however many times it is retried.");
            core.Dispose();
        }

        [TestCase(PPEType.Boots, Description = "current step")]
        [TestCase(PPEType.Helmet, Description = "owned by no equip step (a distractor's job)")]
        public void RecordPpeOutOfOrder_NotAheadOfCurrentStep_PublishesNothing(PPEType type)
        {
            var core = StartPpeSequence(TaskExecutionModeShared.Sequential);

            Assert.IsFalse(core.RecordPpeOutOfOrder(type));
            Assert.IsFalse(_violations.Any(x => x.ViolationCode == ViolationCodes.PpeOutOfOrder));
            core.Dispose();
        }

        [Test]
        public void RecordPpeOutOfOrder_FreeOrderGroup_PublishesNothing()
        {
            var core = StartPpeSequence(TaskExecutionModeShared.FreeOrder);

            Assert.IsFalse(core.RecordPpeOutOfOrder(PPEType.Harness));
            Assert.IsFalse(_violations.Any(x => x.ViolationCode == ViolationCodes.PpeOutOfOrder));
            core.Dispose();
        }

        [Test]
        public void RecordPpeOutOfOrder_AfterResetSession_LogsAgain()
        {
            var core = StartPpeSequence(TaskExecutionModeShared.Sequential);
            core.RecordPpeOutOfOrder(PPEType.Harness);

            core.ResetSession();
            core.StartSession();

            Assert.IsTrue(core.RecordPpeOutOfOrder(PPEType.Harness));
            core.Dispose();
        }

        private TaskManagerCore StartTwoTaskSessionWithDelay(FakeAsyncScheduler scheduler)
        {
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential,
                _tasks.Task("t1", "action_a"),
                _tasks.Task("t2", "action_b"));
            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group },
                () => SessionMode.Guided, scheduler: scheduler, delayBetweenTasks: 2f);
            core.Subscribe();
            core.StartSession();
            return core;
        }

        private void CompleteCurrentTask(TaskManagerCore core)
        {
            var current = core.CurrentRuntimeTask!;
            current.State = TaskState.CompletedSuccess;
            _bus.Publish(new TaskEventArgs(current.TaskData, current, TaskPhase.Completed));
        }

        [Test]
        public void ResetDuringInterTaskDelay_DoesNotEndSession_WhenSchedulerThrows()
        {
            var scheduler = new FakeAsyncScheduler { ThrowOnCancel = true };
            var core = StartTwoTaskSessionWithDelay(scheduler);
            CompleteCurrentTask(core);
            Assert.IsTrue(scheduler.HasPending, "the inter-task delay should be pending");

            core.ResetSession();
            scheduler.Complete();

            Assert.IsEmpty(_sessionCompletions, "a reset must not publish a session summary");
            Assert.IsEmpty(_sessionEnded);
            core.Dispose();
        }

        [Test]
        public void ResetDuringInterTaskDelay_DoesNotEndSession_WhenSchedulerCompletesSilently()
        {
            var scheduler = new FakeAsyncScheduler { ThrowOnCancel = false };
            var core = StartTwoTaskSessionWithDelay(scheduler);
            CompleteCurrentTask(core);
            Assert.IsTrue(scheduler.HasPending, "the inter-task delay should be pending");

            core.ResetSession();
            scheduler.Complete();

            Assert.IsEmpty(_sessionCompletions, "a silently cancelled delay must not resume the old session");
            Assert.IsEmpty(_sessionEnded);
            Assert.IsNull(core.CurrentRuntimeTask);
            core.Dispose();
        }

        [Test]
        public void InterTaskDelay_WhenItElapses_StartsTheNextTask()
        {
            var scheduler = new FakeAsyncScheduler();
            var core = StartTwoTaskSessionWithDelay(scheduler);
            CompleteCurrentTask(core);

            scheduler.Complete();

            Assert.AreEqual("t2", core.CurrentRuntimeTask!.TaskData.taskName);
            core.Dispose();
        }

        [Test]
        public void StartSession_WithOneGroupOneTask_PublishesGroupStartedThenTaskStarted()
        {
            var task = _tasks.Task("t1", "action_a");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, task);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
            core.Subscribe();
            core.StartSession();

            // Read cross-type order from the single interleaved publish log.
            var lifecycle = _bus.PublishedEvents
                .Where(e => e.payload is TaskGroupEventArgs || e.payload is TaskEventArgs)
                .Select(e => e.payload)
                .ToList();

            Assert.AreEqual(2, lifecycle.Count);
            Assert.IsInstanceOf<TaskGroupEventArgs>(lifecycle[0], "Group must start before the task.");
            Assert.IsInstanceOf<TaskEventArgs>(lifecycle[1], "Task must start after the group.");

            var groupStarted = (TaskGroupEventArgs)lifecycle[0];
            var taskStarted = (TaskEventArgs)lifecycle[1];
            Assert.AreEqual(TaskGroupPhase.Started, groupStarted.Phase);
            Assert.AreEqual("g1", groupStarted.Group!.groupName);
            Assert.AreEqual(TaskPhase.Started, taskStarted.Phase);
            Assert.AreEqual("t1", taskStarted.Task.taskName);

            core.Dispose();
        }

        [Test]
        public void TaskCompleted_AdvancesToNextTaskInSameGroup()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
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

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
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
        public void DuplicateCompletionAndClose_PublishOneTerminalPair()
        {
            var task = _tasks.Task("t1", "action_a");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, task);
            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
            core.Subscribe();
            core.StartSession();

            var completion = new TaskEventArgs(task,
                new RuntimeSafetyTask(task) { State = TaskState.CompletedSuccess }, TaskPhase.Completed);
            _bus.Publish(completion);
            _bus.Publish(completion);
            core.CloseCurrentGroup();

            Assert.AreEqual(1, _sessionCompletions.Count);
            Assert.AreEqual(1, _sessionEnded.Count);
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

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { groupA, groupB }, () => SessionMode.Guided);
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

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
            core.Subscribe();
            core.StartSession();

            var found = core.FindPendingTaskByActionId("action_a");
            Assert.IsNotNull(found);
            Assert.AreEqual("t1", found!.taskName);

            var notFound = core.FindPendingTaskByActionId("action_nonexistent");
            Assert.IsNull(notFound);

            core.Dispose();
        }

        [Test]
        public void CloseCurrentGroup_ClosesPendingAsNotPerformed_AndCompletesGroup()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
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

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
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

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
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
            // A group closed with an open task must still advance; depends on CheckGroupCompletion treating NotPerformed as terminal.
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var groupA = _tasks.Group("gA", TaskExecutionModeShared.FreeOrder, t1, t2);

            var t3 = _tasks.Task("t3", "action_c");
            var groupB = _tasks.Group("gB", TaskExecutionModeShared.FreeOrder, t3);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { groupA, groupB }, () => SessionMode.Guided);
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

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
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

        [Test]
        public void GetCompletionOrderDeviations_InOrder_ReturnsEmpty()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var t3 = _tasks.Task("t3", "action_c");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2, t3);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
            core.Subscribe();
            core.StartSession();

            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess, CompletionTime = 1f }, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t2, new RuntimeSafetyTask(t2) { State = TaskState.CompletedSuccess, CompletionTime = 2f }, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t3, new RuntimeSafetyTask(t3) { State = TaskState.CompletedSuccess, CompletionTime = 3f }, TaskPhase.Completed));

            Assert.IsEmpty(core.GetCompletionOrderDeviations("g1"));

            core.Dispose();
        }

        [Test]
        public void GroupThatCompletesNaturally_StopsBeingCurrent_ButIsReportedCompleted()
        {
            // The advance gate must use IsGroupCompleted: the core moves on to the next group as soon as the last task lands.
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var g1 = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1);
            var g2 = _tasks.Group("g2", TaskExecutionModeShared.Sequential, t2);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { g1, g2 }, () => SessionMode.Guided);
            core.Subscribe();
            core.StartSession();

            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess, CompletionTime = 1f }, TaskPhase.Completed));

            Assert.AreEqual("g2", core.GetCurrentGroup()!.id, "the core advances past a completed group");
            Assert.IsTrue(core.IsGroupCompleted("g1"));
            Assert.IsFalse(core.IsGroupCompleted("g2"));
            Assert.IsFalse(core.IsGroupCompleted("nao_existe"));

            core.Dispose();
        }

        [Test]
        public void GetCompletionOrderDeviations_ByGroupId_MeasuresThatGroupAfterItCompleted()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var t3 = _tasks.Task("t3", "action_c");
            var later = _tasks.Task("later", "action_d");
            var g1 = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2, t3);
            var g2 = _tasks.Group("g2", TaskExecutionModeShared.FreeOrder, later);

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { g1, g2 }, () => SessionMode.Guided);
            core.Subscribe();
            core.StartSession();

            // g1 completes out of order (t3 before t2); the out-of-order check names t3, not t2.
            _bus.Publish(new TaskEventArgs(t1, new RuntimeSafetyTask(t1) { State = TaskState.CompletedSuccess, CompletionTime = 1f }, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t2, new RuntimeSafetyTask(t2) { State = TaskState.CompletedSuccess, CompletionTime = 2f }, TaskPhase.Completed));
            _bus.Publish(new TaskEventArgs(t3, new RuntimeSafetyTask(t3) { State = TaskState.CompletedSuccess, CompletionTime = 1.5f }, TaskPhase.Completed));

            Assert.AreEqual("g2", core.GetCurrentGroup()!.id);
            Assert.IsEmpty(core.GetCompletionOrderDeviations("g2"), "g2 has no completions yet");

            var deviations = core.GetCompletionOrderDeviations("g1");
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

            var core = new TaskManagerCore(_bus, _score, new List<ITaskGroup> { group }, () => SessionMode.Guided);
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

            // t4 stays NotStarted (pending) — a task with no completion at all must be skipped
            // rather than read as a deviation.
            Assert.IsEmpty(core.GetCompletionOrderDeviations("g1"));

            core.Dispose();
        }
    }
}
