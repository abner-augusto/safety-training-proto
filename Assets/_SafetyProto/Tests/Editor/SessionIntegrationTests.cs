#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scenarios;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// End-to-end integration battery for the engine-independent domain stack. Unlike the
    /// existing *Core unit tests (which exercise one component with the rest hand-fed), each case
    /// here wires the FULL stack — <c>TaskManagerCore + ScoreService + SafetyRuleEngineCore +
    /// ScoreRuleEngineCore</c> — through ONE real in-process
    /// <c>FakeEventBus</c> and drives it the way a player would, asserting the emergent behavior
    /// of the components talking to each other.
    ///
    /// DRIVER / STUB framing is documented in detail on <see cref="SessionTestHarness"/>:
    ///  • DRIVER = this fixture's method bodies, via the harness's WearPpe/Attempt/ReplayScript —
    ///    the scripted actor standing in for the VR player.
    ///  • STUBS  = FakeEventBus (deterministic in-process stand-in for Unity's deferred EventBus
    ///    asset) and the PPE-sensor events themselves (stubbing the PPE state callbacks).
    ///  • Everything else is the real production domain code, unmodified.
    ///
    /// Headless by construction: this file and all of its Support dependencies reference zero
    /// UnityEngine types, so the SAME source compiles into the standalone .NET test project (T3).
    /// </summary>
    public class SessionIntegrationTests
    {
        private FakeTaskBuilder _tasks = null!;

        [SetUp]
        public void Setup()
        {
            _tasks = new FakeTaskBuilder();
        }

        // Identical scenario and script must complete every task, score the sum of the scenario's own
        // per-risk points, and keep the exact milestone order.
        [Test]
        public void HappyPath_FullPpeInspectionScenario_MatchesCliParity()
        {
            var load = ScenarioLoader.Parse(ReadCanonicalScenario());
            Assert.IsTrue(load.Success, "Canonical scenario should load cleanly. " + load.ErrorSummary);
            var scenario = load.Scenario!;

            using var h = new SessionTestHarness((IReadOnlyList<ITaskGroup>)scenario.Groups,
                scenario.Name, scenario.ParticipantId);
            h.StartSession();
            h.ReplayScript(scenario.Script);

            // Derived from the scenario and the production ScoringConfig, so retuning either
            // does not stale this test. No penalties under full PPE compliance.
            var tasks = scenario.Groups.SelectMany(g => g.TaskDefs).ToList();
            var expectedScore = tasks.Sum(t => scenario.Scoring.PointsFor(t.riskLevel));
            Assert.AreEqual(expectedScore, h.Score.CurrentScore, "Final score should match the CLI parity run.");

            var summary = h.TaskManager.LastSessionSummary;
            Assert.IsTrue(summary.HasValue, "Session should have completed and produced a summary.");
            Assert.AreEqual(tasks.Count, summary!.Value.totalTasks);
            Assert.AreEqual(tasks.Count, summary.Value.tasksCompleted, "Every task should complete successfully.");

            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.CompletedSuccess),
                "Every task should be CompletedSuccess in a fully compliant run.");

            var milestones = h.Bus.MilestoneTokens();
            CollectionAssert.AreEqual(new[]
            {
                "SessionStarted",
                "Group:Started:Seleção de EPIs",
                "Group:Completed:Seleção de EPIs",
                "Group:Started:Inspeção em Andaime Fachadeiro",
                "Group:Completed:Inspeção em Andaime Fachadeiro",
                "SessionCompleted",
                "SessionEnded",
            }, milestones);

            h.Bus.AssertPublishCount<SafetyViolationEventArgs>(0);
        }

        private static string ReadCanonicalScenario()
        {
            const string relativePath = "Assets/_SafetyProto/Resources/Scenarios/default.json";
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                $"Canonical scenario not found from '{TestContext.CurrentContext.TestDirectory}'.",
                relativePath);
        }

        // Guards the end-to-end path: SafetyRuleEngineCore completes with RuntimeTask=null and
        // WasPpeCompliant=false, and ScoreRuleEngineCore must apply the unsafe factor from that flag.
        [Test]
        public void PpeViolation_ActionWithoutRequiredPpe_RaisesViolationAppliesPenalty()
        {
            var task = _tasks.Task("Conectar Talabarte", "connect_harness", PPEType.Harness, PPEType.Helmet);
            task.riskLevel = RiskLevel.Substantial;
            var group = _tasks.Group("Inspeção", TaskExecutionModeShared.FreeOrder, task);

            using var h = new SessionTestHarness(new List<ITaskGroup> { group });
            h.StartSession();

            h.Attempt("connect_harness");

            var violations = h.Bus.EventsOf<SafetyViolationEventArgs>();
            Assert.AreEqual(1, violations.Count, "Exactly one PPE violation expected.");
            Assert.AreEqual("PPE_MISSING", violations[0].ViolationCode);

            Assert.AreEqual(TaskState.CompletedSuccessButUnsafe, h.SessionTasks[0].State);

            Assert.AreEqual(0, h.Score.CurrentScore,
                "Unsafe critical completion must earn nothing end-to-end.");
        }

        [Test]
        public void Sequential_OutOfOrderAction_IsRejected()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1, t2);

            using var h = new SessionTestHarness(new List<ITaskGroup> { group });
            h.StartSession();

            h.Attempt("action_b");

            var violations = h.Bus.EventsOf<SafetyViolationEventArgs>();
            Assert.AreEqual(1, violations.Count);
            Assert.AreEqual("WRONG_ACTION", violations[0].ViolationCode);

            h.Bus.AssertPublishCount<SessionCompletedEventArgs>(0);
            Assert.AreEqual(TaskState.InProgress, h.SessionTasks[0].State);
            Assert.AreEqual(TaskState.NotStarted, h.SessionTasks[1].State);

            h.Attempt("action_a");
            h.Attempt("action_b");
            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.CompletedSuccess));
            h.Bus.AssertPublishCount<SessionCompletedEventArgs>(1);
        }

        [Test]
        public void FreeOrder_AnyOrderCompletion_IsAccepted()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var t3 = _tasks.Task("t3", "action_c");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2, t3);

            using var h = new SessionTestHarness(new List<ITaskGroup> { group });
            h.StartSession();

            h.Attempt("action_c");
            h.Attempt("action_a");
            h.Attempt("action_b");

            h.Bus.AssertPublishCount<SafetyViolationEventArgs>(0);
            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.CompletedSuccess));
            h.Bus.AssertPublishCount<SessionCompletedEventArgs>(1);
        }

        // StartNextGroup never revisits a skipped group, so Group 2 stays skipped forever. Do not
        // reorder the groups: the dependency must come after its dependent, or deleting the
        // dependency check would not fail this test.
        [Test]
        public void GroupDependency_UnmetDependency_SkipsGroupPermanently()
        {
            var g1Task = _tasks.Task("g1t", "action_g1");
            var group1 = _tasks.Group("group1", TaskExecutionModeShared.Sequential, g1Task);

            var g2Task = _tasks.Task("g2t", "action_g2");
            var group2 = _tasks.Group("group2", TaskExecutionModeShared.Sequential, g2Task);

            var g3Task = _tasks.Task("g3t", "action_g3");
            var group3 = _tasks.Group("group3", TaskExecutionModeShared.Sequential, g3Task);

            group2.requiredGroups = new List<ITaskGroup> { group3 };

            using var h = new SessionTestHarness(new List<ITaskGroup> { group1, group2, group3 });
            h.StartSession();

            Assert.AreEqual("group1", h.TaskManager.GetCurrentGroup()?.groupName);
            h.Attempt("action_g1");

            Assert.AreEqual("group3", h.TaskManager.GetCurrentGroup()?.groupName,
                "Group 2's unmet dependency on Group 3 must skip Group 2 and select Group 3 instead.");

            var startedGroups = h.Bus.EventsOf<TaskGroupEventArgs>()
                .Where(g => g.Phase == TaskGroupPhase.Started).Select(g => g.Group?.groupName).ToList();
            CollectionAssert.DoesNotContain(startedGroups, "group2");

            h.Attempt("action_g3");

            Assert.AreEqual(TaskState.CompletedSuccess, h.SessionTasks[0].State);
            Assert.AreEqual(TaskState.NotStarted, h.SessionTasks[1].State, "Group 2's task must never start.");
            Assert.AreEqual(TaskState.CompletedSuccess, h.SessionTasks[2].State);

            var summary = h.TaskManager.LastSessionSummary;
            Assert.IsTrue(summary.HasValue, "Session should end once no runnable group remains.");
            Assert.AreEqual(3, summary!.Value.totalTasks);
            Assert.AreEqual(2, summary.Value.tasksCompleted, "Group 2's task must not count as completed.");
        }

        // Same entry point the Runtime timer bridge uses; a timeout must still reach a terminal event.
        [Test]
        public void Timeout_GroupTimeout_DispatchesSessionEnded()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1, t2);

            using var h = new SessionTestHarness(new List<ITaskGroup> { group });
            h.StartSession();

            h.Bus.AssertPublishCount<SessionEndedEventArgs>(0);
            h.TaskManager.HandleGroupTimeout();

            h.Bus.AssertPublishCount<SessionEndedEventArgs>(1);
            h.Bus.AssertPublishCount<SessionCompletedEventArgs>(1);

            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.NotPerformed));

            var summary = h.TaskManager.LastSessionSummary;
            Assert.IsTrue(summary.HasValue);
            Assert.AreEqual(0, summary!.Value.tasksCompleted, "No tasks completed successfully on timeout.");
            Assert.AreEqual(2, summary.Value.totalTasks);
        }

        // Unknown action ids are validated at runtime (ActionResolver), not by ScenarioLoader.
        [Test]
        public void LoadValidation_UnknownPpe_ReturnsFailureDoesNotThrow()
        {
            const string json = @"{
              ""name"": ""Bad"",
              ""groups"": [
                { ""name"": ""g1"", ""executionMode"": ""Sequential"", ""tasks"": [
                  { ""name"": ""t1"", ""requiredPPE"": [""Jetpack""] }
                ]}
              ]
            }";

            ScenarioLoadResult result = null!;
            Assert.DoesNotThrow(() => result = ScenarioLoader.Parse(json));
            Assert.IsFalse(result.Success, "Unknown PPE type should fail validation.");
            Assert.IsNull(result.Scenario);
            Assert.IsNotEmpty(result.Errors);
            StringAssert.Contains("Jetpack", result.ErrorSummary);
        }

        [Test]
        public void LoadValidation_MalformedJson_ReturnsFailureDoesNotThrow()
        {
            const string malformed = @"{ ""name"": ""Bad"", ""groups"": [ ";

            ScenarioLoadResult result = null!;
            Assert.DoesNotThrow(() => result = ScenarioLoader.Parse(malformed));
            Assert.IsFalse(result.Success, "Malformed JSON should fail, not throw.");
            Assert.IsNull(result.Scenario);
            Assert.IsNotEmpty(result.Errors);
        }
    }
}
