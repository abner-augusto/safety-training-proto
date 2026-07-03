#nullable enable
using System.Collections.Generic;
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
    /// ScoreRuleEngineCore + SessionLoggerCore</c> — through ONE real in-process
    /// <c>FakeEventBus</c> and drives it the way a player would, asserting the emergent behavior
    /// of the components talking to each other.
    ///
    /// DRIVER / STUB framing (Reviewer E) is documented in detail on <see cref="SessionTestHarness"/>:
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

        // ── Case 1: Happy-path parity ────────────────────────────────────────────────────────
        // Loads the REAL PPEInspection scenario from JSON (Tools/CliHarness/scenarios/
        // ppe_inspection.json, embedded via PpeInspectionScenarioJson) and replays its own
        // scripted playthrough. This codifies the previously-manual "does the domain stack match
        // the CLI harness run?" parity check as an automated assertion: identical scenario +
        // identical script ⇒ 9/9 tasks, score 1400, and the exact lifecycle milestone order.
        [Test]
        public void HappyPath_FullPpeInspectionScenario_MatchesCliParity()
        {
            var load = ScenarioLoader.Parse(PpeInspectionScenarioJson.Value);
            Assert.IsTrue(load.Success, "PPEInspection scenario should load cleanly. " + load.ErrorSummary);
            var scenario = load.Scenario!;

            using var h = new SessionTestHarness((IReadOnlyList<ITaskGroup>)scenario.Groups,
                scenario.Name, scenario.ParticipantId);
            h.StartSession();
            h.ReplayScript(scenario.Script);

            // Final score parity with the CLI harness run (sum of all successPoints; no penalties
            // because every task is completed with full PPE compliance).
            Assert.AreEqual(1400, h.Score.CurrentScore, "Final score should match the CLI parity run.");

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

        // ── Case 2: PPE violation ────────────────────────────────────────────────────────────
        // Action performed while a required PPE is missing. Asserts the SafetyViolation fires, the
        // task is recorded as CompletedSuccessButUnsafe, AND the ppePenalty is subtracted from the
        // score end-to-end (successPoints - ppePenalty).
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
            task.successPoints = 200;
            task.ppePenalty = 50;
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

            // End-to-end score reflects the penalty: successPoints (200) - ppePenalty (50) = 150.
            Assert.AreEqual(150, h.Score.CurrentScore,
                "ppePenalty must be applied end-to-end for a non-compliant completion.");
        }

        // ── Case 3: Sequential ordering ──────────────────────────────────────────────────────
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

        // ── Case 4: FreeOrder ────────────────────────────────────────────────────────────────
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

        // ── Case 5: Group dependency gating ──────────────────────────────────────────────────
        // Group 2 declares Group 1 as a required predecessor. Group 1 starts first; a Group-2
        // action attempted before Group 1 completes is blocked (Group 2 is not yet active), and
        // Group 2 only starts once Group 1 finishes.
        [Test]
        public void GroupDependency_Group2ActionBeforeGroup1Completes_IsBlocked()
        {
            var g1Task = _tasks.Task("g1t", "action_g1");
            var group1 = _tasks.Group("group1", TaskExecutionModeShared.Sequential, g1Task);

            var g2Task = _tasks.Task("g2t", "action_g2");
            var group2 = _tasks.Group("group2", TaskExecutionModeShared.Sequential, g2Task);
            group2.requiredGroups = new List<ITaskGroup> { group1 };

            using var h = new SessionTestHarness(new List<ITaskGroup> { group1, group2 });
            h.StartSession();

            // Group 1 is active; Group 2 has not started.
            Assert.AreEqual("group1", h.TaskManager.GetCurrentGroup()?.groupName);

            // Attempt Group 2's action early → rejected (does not match Group 1's active task),
            // and Group 2's task stays NotStarted.
            h.Attempt("action_g2");
            var violations = h.Bus.EventsOf<SafetyViolationEventArgs>();
            Assert.AreEqual(1, violations.Count);
            Assert.AreEqual("WRONG_ACTION", violations[0].ViolationCode);
            Assert.AreEqual(TaskState.NotStarted, h.SessionTasks[1].State, "Group 2 task must stay blocked.");

            // Group 2 must not have started yet.
            var startedGroupsBefore = h.Bus.EventsOf<TaskGroupEventArgs>()
                .Where(g => g.Phase == TaskGroupPhase.Started).Select(g => g.Group?.groupName).ToList();
            CollectionAssert.DoesNotContain(startedGroupsBefore, "group2");

            // Complete Group 1 → Group 2 unlocks and can now be completed.
            h.Attempt("action_g1");
            Assert.AreEqual("group2", h.TaskManager.GetCurrentGroup()?.groupName);
            h.Attempt("action_g2");
            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.CompletedSuccess));
            h.Bus.AssertPublishCount<SessionCompletedEventArgs>(1);
        }

        // ── Case 6: Timeout path (T1 regression) ─────────────────────────────────────────────
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

            // Unfinished tasks were force-failed by the timeout cascade.
            Assert.IsTrue(h.SessionTasks.All(t => t.State == TaskState.CompletedFailure));

            var summary = h.TaskManager.LastSessionSummary;
            Assert.IsTrue(summary.HasValue);
            Assert.AreEqual(0, summary!.Value.tasksCompleted, "No tasks completed successfully on timeout.");
            Assert.AreEqual(2, summary.Value.totalTasks);
        }

        // ── Case 7: Load validation ──────────────────────────────────────────────────────────
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
