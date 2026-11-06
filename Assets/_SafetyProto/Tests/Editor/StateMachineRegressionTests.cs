#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Sessions;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Session state transitions that must stay unreachable. Each case drives the full domain
    /// stack through the deferred bus, the way a headset does, and asserts the invariant the
    /// transition broke.
    /// </summary>
    public class StateMachineRegressionTests
    {
        private FakeTaskBuilder _tasks = null!;

        [SetUp]
        public void Setup() => _tasks = new FakeTaskBuilder();

        private static int CompletionsOf(SessionTestHarness h, string taskName) =>
            h.Bus.EventsOf<TaskEventArgs>()
                .Count(e => e.Phase == TaskPhase.Completed && e.Task.taskName == taskName);

        // A Sequential task stays the rule engine's active task for the whole inter-task delay.
        // Repeating its action then (a second drill press, a second report) must not complete
        // it again or pay its points twice.
        [Test]
        public void Sequential_RepeatedActionDuringInterTaskDelay_CompletesAndScoresOnce()
        {
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential,
                _tasks.Task("t1", "action_a"),
                _tasks.Task("t2", "action_b"));
            var scheduler = new FakeAsyncScheduler();
            using var h = new SessionTestHarness(new List<ITaskGroup> { group },
                scheduler: scheduler, delayBetweenTasks: 2f);
            h.StartSession();

            h.Attempt("action_a");
            int scoreAfterFirst = h.Score.CurrentScore;
            Assert.IsTrue(scheduler.HasPending, "the inter-task delay should be running");

            h.Attempt("action_a");

            Assert.AreEqual(1, CompletionsOf(h, "t1"), "a completed task must not complete again");
            Assert.AreEqual(scoreAfterFirst, h.Score.CurrentScore, "a repeat must not pay twice");
            h.Bus.AssertPublishCount<SafetyViolationEventArgs>(0);

            scheduler.Complete();
            h.Attempt("action_b");
            scheduler.Complete();
            Assert.IsTrue(h.TaskManager.LastSessionSummary.HasValue, "the session should still finish");
        }

        // A reset with no session in this scene (waiting screen, or the second of two resets in
        // one frame) must not write a log: it would be an id-less, completed=false file in the
        // collection folder.
        [Test]
        public void LoggerReset_WithNoSessionStarted_WritesNoLog()
        {
            var bus = new FakeEventBus();
            var outputDir = Path.Combine(Path.GetTempPath(), "safetyproto_smtests_" + Guid.NewGuid().ToString("N"));
            try
            {
                using var logger = new SessionLoggerCore(
                    bus, outputDir, SessionLoggerCore.SerializeIndentedOmittingDefaults, () => SessionMode.Guided);
                logger.Subscribe();

                bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 1, TimestampMs = 1000L });
                logger.ResetSession();
                Assert.IsTrue(SpinWait.SpinUntil(() => LogFiles(outputDir).Length == 1, 5000),
                    "the reset of a started session must still leave its partial log");

                logger.ResetSession();
                Thread.Sleep(200);

                Assert.AreEqual(1, LogFiles(outputDir).Length, "a reset with no session must not write a log");
            }
            finally
            {
                if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
            }
        }

        // The Evaluation gate captures its pending list on the press. A task completed while the
        // confirmation is open (a lanyard locked) must not stage its consequence on "Iniciar",
        // or an anchored participant gets a fall and a critical failure.
        [Test]
        public void GateConfirm_DropsTasksCompletedWhileTheConfirmationWasOpen()
        {
            var group = _tasks.Group("inspecao", TaskExecutionModeShared.FreeOrder,
                _tasks.Task("connect_lanyard", "connect_harness"),
                _tasks.Task("inspect_mesh", "report_damaged_safety_mesh"));
            using var h = new SessionTestHarness(new List<ITaskGroup> { group }, mode: SessionMode.Evaluation);
            h.StartSession();
            var capturedAtPress = h.SessionTasks.Where(t => !Domain.Tasks.TaskManagerCore.IsTerminal(t.State)).ToList();
            Assert.AreEqual(2, capturedAtPress.Count);

            h.Attempt("connect_harness");

            var stillPending = Domain.Safety.GateFinish.StillPending(capturedAtPress);
            CollectionAssert.AreEqual(new[] { "inspect_mesh" }, stillPending.Select(t => t.taskName).ToArray());
        }

        // A start_session from a second tab, an ack-timeout retry, or one racing a keypad start
        // can reach a session that has just begun. Unless the evaluator confirmed the
        // replacement it must be refused, not leave a short partial log and a spurious attempt.
        [Test]
        public void StartSession_OverARunningSessionTheEvaluatorNeverSaw_IsRefused()
        {
            Assert.AreEqual(Domain.Dashboard.StartSessionAction.RefuseLive,
                Domain.Dashboard.StartSessionPolicy.Decide(sessionStarted: true, replaceConfirmed: false));
            Assert.AreEqual(Domain.Dashboard.StartSessionAction.Replace,
                Domain.Dashboard.StartSessionPolicy.Decide(sessionStarted: true, replaceConfirmed: true));
            Assert.AreEqual(Domain.Dashboard.StartSessionAction.StartNow,
                Domain.Dashboard.StartSessionPolicy.Decide(sessionStarted: false, replaceConfirmed: true),
                "a replace that finds the waiting screen (the session was reset meanwhile) just starts");
        }

        // A warning (a decoy snapped on a body slot) must not replace an open prompt such as
        // "Fase concluída": the prompt's callback would be dropped, and Guided mode has no other
        // way out of the PPE room.
        [Test]
        public void Notice_DoesNotReplaceAnOpenPrompt()
        {
            Assert.IsFalse(Domain.Feedback.PopupPreemption.MayReplace(promptShowing: true, incomingIsPrompt: false));
            Assert.IsTrue(Domain.Feedback.PopupPreemption.MayReplace(promptShowing: true, incomingIsPrompt: true));
            Assert.IsTrue(Domain.Feedback.PopupPreemption.MayReplace(promptShowing: false, incomingIsPrompt: false));
        }

        private static SessionStartRequest PendingRestartOfP07() =>
            SessionLineage.Next(new SessionSnapshot
            {
                Started = true, SessionId = "S1", ParticipantId = "P-07",
                Source = ParticipantSources.Dashboard, Mode = SessionMode.Evaluation, Attempt = 1
            }, requested: null)!;

        // A second reset that lands before the reloaded scene consumed its restart sees a scene
        // that never started a session. It must keep the pending restart, or the interrupted
        // participant goes back to the waiting screen instead of restarting.
        [Test]
        public void ResetBeforeTheRestartWasConsumed_KeepsThePendingRestart()
        {
            var pending = PendingRestartOfP07();

            var next = SessionLineage.Next(new SessionSnapshot(), requested: null, carried: pending);

            Assert.IsNotNull(next);
            Assert.AreEqual("P-07", next!.ParticipantId);
            Assert.AreEqual("S1", next.PreviousSessionId);
            Assert.AreEqual(2, next.Attempt);
        }

        // An evaluator start that lands in the same window replaces the pending restart and must
        // keep its link to the interrupted session.
        [Test]
        public void EvaluatorStartOverAPendingRestart_StaysChainedToTheInterruptedSession()
        {
            var pending = PendingRestartOfP07();

            var other = SessionLineage.Supersede(pending,
                new SessionStartRequest("P-08", ParticipantSources.Dashboard, SessionMode.Guided));
            Assert.AreEqual("P-08", other.ParticipantId);
            Assert.AreEqual("S1", other.PreviousSessionId);
            Assert.AreEqual(1, other.Attempt, "another code starts at 1");

            var same = SessionLineage.Supersede(pending,
                new SessionStartRequest("P-07", ParticipantSources.Dashboard, SessionMode.Guided));
            Assert.AreEqual(SessionMode.Guided, same.Mode);
            Assert.AreEqual("S1", same.PreviousSessionId);
            Assert.AreEqual(2, same.Attempt, "the same code keeps the attempt it was restarting");

            var viaReset = SessionLineage.Next(new SessionSnapshot(),
                new SessionStartRequest("P-08", ParticipantSources.Dashboard, SessionMode.Guided), carried: pending);
            Assert.AreEqual("S1", viaReset!.PreviousSessionId);
        }

        private static string[] LogFiles(string dir) =>
            Directory.Exists(dir) ? Directory.GetFiles(dir, "session_log_*.json") : Array.Empty<string>();
    }
}
