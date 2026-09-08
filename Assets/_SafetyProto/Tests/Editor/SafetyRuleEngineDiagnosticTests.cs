using System.Collections.Generic;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Safety;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class SafetyRuleEngineDiagnosticTests
    {
        private FakeEventBus _bus = null!;
        private FakeTaskBuilder _tasks = null!;
        private List<SafetyViolationEventArgs> _violations = null!;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _tasks = new FakeTaskBuilder();
            _violations = new List<SafetyViolationEventArgs>();
            _bus.Subscribe<SafetyViolationEventArgs>(v => _violations.Add(v));
        }

        [Test]
        public void Diagnostic_ActionAfterGroupStarted_PublishesCompletion_NoActiveGroupViolation()
        {
            // Merges two historical Play-mode diagnostics that each only asserted the ABSENCE of
            // NO_ACTIVE_GROUP — which passed even if the engine silently did nothing at all (an
            // empty violations list satisfies "no violation says NO_ACTIVE_GROUP" trivially).
            // The completion assertion below is what makes the test fail if the engine stops
            // reacting at all, not only if it reacts with the specific wrong violation code.
            //
            // Runs with TWO engine instances subscribed to the same bus — the stronger of the
            // two original arrangements — to also cover the handler-ordering / Delegate.Combine
            // bug where one core could receive ActionAttempted before GroupStarted.
            var coreA = new SafetyRuleEngineCore(_bus);
            var coreB = new SafetyRuleEngineCore(_bus);
            coreA.Subscribe();
            coreB.Subscribe();

            var completions = new List<TaskEventArgs>();
            _bus.Subscribe<TaskEventArgs>(t =>
            {
                if (t.Phase == TaskPhase.Completed) completions.Add(t);
            });

            var task = _tasks.Task("ppe_gloves", "ppe.putongloves", PPEType.GloveLeft);
            var group = _tasks.Group("PPE Check", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.GloveLeft, true));
            _bus.Publish(new ActionAttemptedEvent("ppe.putongloves"));

            // Each engine owns its own state and neither guards against a second instance also
            // completing the same task, so two subscribed engines correctly produce two
            // completions here — this is the expected shape of THIS fixture, not a bug.
            Assert.AreEqual(2, completions.Count,
                "Expected both subscribed engines to publish a TaskCompleted for the matched action.");

            var noGroupViolations = _violations.FindAll(v => v.ViolationCode == "NO_ACTIVE_GROUP");
            Assert.AreEqual(0, noGroupViolations.Count,
                $"Got {noGroupViolations.Count} NO_ACTIVE_GROUP violations — indicates one core received " +
                "ActionAttempted before receiving GroupStarted. Possible handler-ordering or " +
                "Delegate.Combine interaction bug.");

            coreA.Dispose();
            coreB.Dispose();
        }

        [Test]
        public void Diagnostic_GroupCompleted_DoesNotIncorrectlyReactivateGroup()
        {
            var engine = new SafetyRuleEngineCore(_bus);
            engine.Subscribe();

            var task = _tasks.Task("t", "a");
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Completed));

            _bus.Publish(new ActionAttemptedEvent("a"));

            var noGroupViolations = _violations.FindAll(v => v.ViolationCode == "NO_ACTIVE_GROUP");
            Assert.AreEqual(1, noGroupViolations.Count,
                "Expected NO_ACTIVE_GROUP after group completion, but got none — " +
                "_onGroupStarted and _onGroupCompleted are interfering via shared " +
                "Delegate.Combine on typeof(TaskGroupEventArgs).");

            engine.Dispose();
        }

        [Test]
        public void Diagnostic_SubscribeAfterGroupStarted_NeverRecoversForThatGroup()
        {
            // The engine has no resync-on-subscribe: OnGroupStarted is the only place that
            // sets _activeGroup, and OnTaskStarted bails out immediately when _activeGroup is
            // null (SafetyRuleEngineCore.OnTaskStarted). So an engine that subscribes AFTER a
            // group's Started event already fired can never recover for that group — not even
            // once a later task within the same group starts.
            //
            // The original version of this test re-published TaskGroupEventArgs AFTER
            // subscribing, which made the "missed" first publication below irrelevant to the
            // outcome — it passed regardless of whether this recovery gap exists at all.
            var task = _tasks.Task("t", "a", PPEType.Helmet);
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential, task);

            // Group starts before anyone is listening.
            _bus.Publish(new TaskGroupEventArgs(group));

            var engine = new SafetyRuleEngineCore(_bus);
            engine.Subscribe();

            // A later task-started event for the SAME group's task fires, but the engine still
            // has no active group to attach it to.
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, true));
            _bus.Publish(new ActionAttemptedEvent("a"));

            var noGroupViolations = _violations.FindAll(v => v.ViolationCode == "NO_ACTIVE_GROUP");
            Assert.AreEqual(1, noGroupViolations.Count,
                "A late-subscribing engine has no resync mechanism, so it must report " +
                "NO_ACTIVE_GROUP even after a later TaskEventArgs(Started) for the missed " +
                "group's task. If this now fails, a resync path was added and this test's name " +
                "and assertion need to flip to match the new, better behavior.");

            engine.Dispose();
        }
    }
}
