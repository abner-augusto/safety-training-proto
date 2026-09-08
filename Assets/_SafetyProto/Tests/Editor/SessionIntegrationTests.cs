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

        // Loads the canonical Unity/CLI scenario JSON and replays its scripted playthrough.
        // This codifies the previously-manual "does the domain stack match
        // the CLI harness run?" parity check as an automated assertion: identical scenario +
        // identical script ⇒ 9/9 tasks, score 1500, and the exact lifecycle milestone order.
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

            // Final score parity with the CLI harness run: the sum of the risk-level points of
            // all 9 default.json tasks, whose levels are derived from the authored severity x
            // probability grades. 1 intolerable x 250 + 3 substantial x 200 + 3 moderate x 150
            // + 2 tolerable x 100 = 1500; no penalties because every task completes with full
            // PPE compliance. (Was 1350 under the retired three-tier severity: the risk matrix
            // raised goggles and toeboard a tier and gave the lanyard its own top tier.)
            Assert.AreEqual(1500, h.Score.CurrentScore, "Final score should match the CLI parity run.");

            var summary = h.TaskManager.LastSessionSummary;
            Assert.IsTrue(summary.HasValue, "Session should have completed and produced a summary.");
            Assert.AreEqual(9, summary!.Value.totalTasks);
            Assert.AreEqual(9, summary.Value.tasksCompleted, "All 9 tasks should complete successfully.");

            // Every runtime task ended in CompletedSuccess (fully compliant run).
            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.CompletedSuccess),
                "Every task should be CompletedSuccess in a fully compliant run.");

            // Ordered lifecycle spine — the stable event SEQUENCE (not just counts).
            var milestones = h.Bus.MilestoneTokens();
            CollectionAssert.AreEqual(new[]
            {
                "SessionStarted",
                "Group:Started:Seleção de EPIs",
                "Group:Completed:Seleção de EPIs",
                "Group:Started:Inspeção em Andaime Fachadeiro",
                "Group:Completed:Inspeção em Andaime Fachadeiro",
                "SessionCompleted",
                "SessionEnded",          // T1: terminal event now fires on normal completion too
            }, milestones);

            // No safety violations in a clean run.
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

        // Action performed while a required PPE is missing. Asserts the SafetyViolation fires, the
        // task is recorded as CompletedSuccessButUnsafe, AND the severity-driven unsafe earning
        // (critical tier earns 0% of its points) applies end-to-end.
        //
        // This case originally surfaced a real integration bug (now fixed): SafetyRuleEngineCore
        // publishes the completion with RuntimeTask=null, signalling non-compliance via
        // WasPpeCompliant=false, but ScoreRuleEngineCore used to derive the unsafe state from
        // RuntimeTask.State alone and so never applied the penalty through the wired stack. The fix
        // makes ScoreRuleEngineCore honor WasPpeCompliant when RuntimeTask is null (its documented
        // role). This test now asserts the corrected end-to-end behavior and guards the regression.
        [Test]
        public void PpeViolation_ActionWithoutRequiredPpe_RaisesViolationAppliesPenalty()
        {
            var task = _tasks.Task("Conectar Talabarte", "connect_harness", PPEType.Harness, PPEType.Helmet);
            task.riskLevel = RiskLevel.Substantial;
            var group = _tasks.Group("Inspeção", TaskExecutionModeShared.FreeOrder, task);

            using var h = new SessionTestHarness(new List<ITaskGroup> { group });
            h.StartSession();

            // No PPE worn → attempt the action anyway.
            h.Attempt("connect_harness");

            var violations = h.Bus.EventsOf<SafetyViolationEventArgs>();
            Assert.AreEqual(1, violations.Count, "Exactly one PPE violation expected.");
            Assert.AreEqual("PPE_MISSING", violations[0].ViolationCode);

            // Task still completes, but flagged unsafe by TaskManagerCore.
            Assert.AreEqual(TaskState.CompletedSuccessButUnsafe, h.SessionTasks[0].State);

            // End-to-end score reflects the critical-tier unsafe factor (0%): unsafe critical
            // completions earn nothing under the default config.
            Assert.AreEqual(0, h.Score.CurrentScore,
                "Unsafe critical completion must earn nothing end-to-end.");
        }

        // In a Sequential group the active task is fixed; attempting the NEXT task's action out of
        // order is rejected as WRONG_ACTION and does not complete anything.
        [Test]
        public void Sequential_OutOfOrderAction_IsRejected()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1, t2);

            using var h = new SessionTestHarness(new List<ITaskGroup> { group });
            h.StartSession();

            // Active task is t1 (action_a). Attempt t2's action out of order.
            h.Attempt("action_b");

            var violations = h.Bus.EventsOf<SafetyViolationEventArgs>();
            Assert.AreEqual(1, violations.Count);
            Assert.AreEqual("WRONG_ACTION", violations[0].ViolationCode);

            // Nothing completed; both tasks still pending, t1 still the in-progress one.
            h.Bus.AssertPublishCount<SessionCompletedEventArgs>(0);
            Assert.AreEqual(TaskState.InProgress, h.SessionTasks[0].State);
            Assert.AreEqual(TaskState.NotStarted, h.SessionTasks[1].State);

            // The correct order then works: action_a completes t1, action_b completes t2.
            h.Attempt("action_a");
            h.Attempt("action_b");
            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.CompletedSuccess));
            h.Bus.AssertPublishCount<SessionCompletedEventArgs>(1);
        }

        // In a FreeOrder group tasks may be completed in ANY order with no violation.
        [Test]
        public void FreeOrder_AnyOrderCompletion_IsAccepted()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var t3 = _tasks.Task("t3", "action_c");
            var group = _tasks.Group("g1", TaskExecutionModeShared.FreeOrder, t1, t2, t3);

            using var h = new SessionTestHarness(new List<ITaskGroup> { group });
            h.StartSession();

            // Deliberately reverse order.
            h.Attempt("action_c");
            h.Attempt("action_a");
            h.Attempt("action_b");

            h.Bus.AssertPublishCount<SafetyViolationEventArgs>(0);
            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.CompletedSuccess));
            h.Bus.AssertPublishCount<SessionCompletedEventArgs>(1);
        }

        // TaskManagerCore.StartNextGroup walks the group list forward and never revisits a
        // skipped index (TaskManagerCore.cs:292-311). Group 2 depends on Group 3, which comes
        // AFTER it in list order and so cannot possibly have completed by the time Group 2's
        // turn arrives — Group 2 is therefore skipped forever and Group 3 starts instead. That
        // arrangement is deliberate: with the groups in dependency-satisfying order the
        // dependency check contributes nothing observable — the plain list order alone would
        // pick the same group next, so deleting the check would not fail the test. Putting the
        // depended-on group AFTER its dependent is what makes "the check ran" and "the check
        // was deleted" diverge. Do not reorder them.
        [Test]
        public void GroupDependency_UnmetDependency_SkipsGroupPermanently()
        {
            var g1Task = _tasks.Task("g1t", "action_g1");
            var group1 = _tasks.Group("group1", TaskExecutionModeShared.Sequential, g1Task);

            var g2Task = _tasks.Task("g2t", "action_g2");
            var group2 = _tasks.Group("group2", TaskExecutionModeShared.Sequential, g2Task);

            var g3Task = _tasks.Task("g3t", "action_g3");
            var group3 = _tasks.Group("group3", TaskExecutionModeShared.Sequential, g3Task);

            // Group 2's dependency is Group 3, which is still two-and-then-one steps away from
            // completing when Group 2's turn comes up.
            group2.requiredGroups = new List<ITaskGroup> { group3 };

            using var h = new SessionTestHarness(new List<ITaskGroup> { group1, group2, group3 });
            h.StartSession();

            Assert.AreEqual("group1", h.TaskManager.GetCurrentGroup()?.groupName);
            h.Attempt("action_g1");

            // Group 2's dependency (Group 3) has not completed, so Group 2 is skipped — Group 3
            // becomes active next, not Group 2. Without the dependency check this would instead
            // be "group2".
            Assert.AreEqual("group3", h.TaskManager.GetCurrentGroup()?.groupName,
                "Group 2's unmet dependency on Group 3 must skip Group 2 and select Group 3 instead.");

            var startedGroups = h.Bus.EventsOf<TaskGroupEventArgs>()
                .Where(g => g.Phase == TaskGroupPhase.Started).Select(g => g.Group?.groupName).ToList();
            CollectionAssert.DoesNotContain(startedGroups, "group2");

            h.Attempt("action_g3");

            // Group 2 is never revisited: the session ends with 2 of 3 tasks completed and
            // Group 2's task permanently NotStarted.
            Assert.AreEqual(TaskState.CompletedSuccess, h.SessionTasks[0].State);
            Assert.AreEqual(TaskState.NotStarted, h.SessionTasks[1].State, "Group 2's task must never start.");
            Assert.AreEqual(TaskState.CompletedSuccess, h.SessionTasks[2].State);

            var summary = h.TaskManager.LastSessionSummary;
            Assert.IsTrue(summary.HasValue, "Session should end once no runnable group remains.");
            Assert.AreEqual(3, summary!.Value.totalTasks);
            Assert.AreEqual(2, summary.Value.tasksCompleted, "Group 2's task must not count as completed.");
        }

        // A group timeout is driven through the same domain entry point the Runtime timer bridge
        // uses (TaskManagerCore.HandleGroupTimeout). Before the T1 fix, a group timeout produced
        // NO terminal event; this asserts SessionEnded (and SessionCompleted) now fire.
        [Test]
        public void Timeout_GroupTimeout_DispatchesSessionEnded()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var group = _tasks.Group("g1", TaskExecutionModeShared.Sequential, t1, t2);

            using var h = new SessionTestHarness(new List<ITaskGroup> { group });
            h.StartSession();

            // No tasks completed — the group's time limit elapses.
            h.Bus.AssertPublishCount<SessionEndedEventArgs>(0);
            h.TaskManager.HandleGroupTimeout();

            // T1: the session is now driven to a terminal state.
            h.Bus.AssertPublishCount<SessionEndedEventArgs>(1);
            h.Bus.AssertPublishCount<SessionCompletedEventArgs>(1);

            // Unfinished tasks were closed by the timeout cascade. Running out of time and
            // skipping the task are the same outcome here — see TaskState.NotPerformed.
            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.NotPerformed));

            var summary = h.TaskManager.LastSessionSummary;
            Assert.IsTrue(summary.HasValue);
            Assert.AreEqual(0, summary!.Value.tasksCompleted, "No tasks completed successfully on timeout.");
            Assert.AreEqual(2, summary.Value.totalTasks);
        }

        // Invalid scenario JSON must return a failure result WITHOUT throwing. Two flavors:
        // an unknown PPE enum name (semantic validation in SafetyTaskDef.Bind) and malformed
        // JSON (structural). NOTE: unknown ACTION ids are not validated by ScenarioLoader — that
        // is a Unity-runtime concern (ActionResolver against Resources), outside the loader — so
        // the loader-level failure case exercises unknown PPE + malformed JSON.
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
