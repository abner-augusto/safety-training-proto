using System.Collections.Generic;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;
using SafetyProto.Gameplay.Events;
using SafetyProto.Gameplay.Safety;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class SafetyRuleEngineDiagnosticTests
    {
        private FakeEventBus _bus;
        private FakeTaskBuilder _tasks;
        private List<SafetyViolationEventArgs> _violations;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _tasks = new FakeTaskBuilder();
            _violations = new List<SafetyViolationEventArgs>();
            _bus.Subscribe<SafetyViolationEventArgs>(v => _violations.Add(v));
        }

        [Test]
        public void Diagnostic_PlayModeTimeline_ShouldPublishTaskCompletion_NotNoActiveGroup()
        {
            var engine = new SafetyRuleEngineCore(_bus);
            engine.Subscribe();

            var task = _tasks.Task("ppe_gloves", "ppe.putongloves", PPEType.Gloves);
            var group = _tasks.Group("PPE Check", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Gloves, true));
            _bus.Publish(new ActionAttemptedEvent("ppe.putongloves"));

            foreach (var v in _violations)
            {
                Assert.AreNotEqual("NO_ACTIVE_GROUP", v.ViolationCode,
                    "Reproduced Play mode bug in pure C# — logic issue, not Unity runtime.");
            }

            engine.Dispose();
        }

        [Test]
        public void Diagnostic_TwoCoresSubscribed_BothReceiveGroupAndAction()
        {
            var coreA = new SafetyRuleEngineCore(_bus);
            var coreB = new SafetyRuleEngineCore(_bus);
            coreA.Subscribe();
            coreB.Subscribe();

            var task = _tasks.Task("t", "action_a", PPEType.Helmet);
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, true));
            _bus.Publish(new ActionAttemptedEvent("action_a"));

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
        public void Diagnostic_SubscribeAfterFirstGroupStarted_StillCapturesLaterActions()
        {
            var task = _tasks.Task("t", "a", PPEType.Helmet);
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));

            var engine = new SafetyRuleEngineCore(_bus);
            engine.Subscribe();

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, true));
            _bus.Publish(new ActionAttemptedEvent("a"));

            Assert.IsEmpty(_violations,
                "Expected no violations after re-subscribing and re-publishing group.");

            engine.Dispose();
        }
    }
}
