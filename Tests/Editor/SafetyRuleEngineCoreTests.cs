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
    public class SafetyRuleEngineCoreTests
    {
        private FakeEventBus _bus;
        private FakeTaskBuilder _tasks;
        private List<TaskEventArgs> _taskCompletions;
        private List<SafetyViolationEventArgs> _violations;
        private SafetyRuleEngineCore _engine;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _tasks = new FakeTaskBuilder();
            _taskCompletions = new List<TaskEventArgs>();
            _violations = new List<SafetyViolationEventArgs>();

            _bus.Subscribe<TaskEventArgs>(args => _taskCompletions.Add(args));
            _bus.Subscribe<SafetyViolationEventArgs>(args => _violations.Add(args));

            _engine = new SafetyRuleEngineCore(_bus);
            _engine.Subscribe();
        }

        [TearDown]
        public void TearDown()
        {
            _engine.Dispose();
        }

        [Test]
        public void SequentialGroup_MatchingAction_PublishesTaskCompleted()
        {
            var task = _tasks.Task("ppe_helmet", "equip_helmet", PPEType.Helmet);
            var group = _tasks.Group("Sequential group", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, isWearing: true));
            _bus.Publish(new ActionAttemptedEvent("equip_helmet"));

            Assert.That(_taskCompletions.Count, Is.GreaterThanOrEqualTo(1));
            var completion = _taskCompletions[_taskCompletions.Count - 1];
            Assert.AreEqual(task.taskName, completion.Task.taskName);
            Assert.IsNotNull(completion.RuntimeTask);
            Assert.AreEqual(TaskState.CompletedSuccess, completion.RuntimeTask.State);
            Assert.IsEmpty(_violations);
        }

        [Test]
        public void SequentialGroup_WrongAction_PublishesWrongActionViolation()
        {
            var task = _tasks.Task("ppe_helmet", "equip_helmet", PPEType.Helmet);
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new ActionAttemptedEvent("equip_boots"));

            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual("WRONG_ACTION", _violations[0].ViolationCode);
            Assert.IsEmpty(_taskCompletions);
        }

        [Test]
        public void ActionWithoutActiveGroup_PublishesNoActiveGroupViolation()
        {
            _bus.Publish(new ActionAttemptedEvent("equip_helmet"));

            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual("NO_ACTIVE_GROUP", _violations[0].ViolationCode);
        }

        [Test]
        public void ActionWithMissingPPE_PublishesPpeMissingViolation_AndUnsafeCompletion()
        {
            var task = _tasks.Task("ppe_helmet", "equip_helmet", PPEType.Helmet);
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new ActionAttemptedEvent("equip_helmet"));

            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual("PPE_MISSING", _violations[0].ViolationCode);
            Assert.AreEqual(1, _taskCompletions.Count);
            Assert.AreEqual(TaskState.CompletedSuccessButUnsafe,
                _taskCompletions[0].RuntimeTask.State);
        }

        [Test]
        public void FreeOrderGroup_TasksCanCompleteInAnyOrder()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var t2 = _tasks.Task("t2", "action_b");
            var t3 = _tasks.Task("t3", "action_c");
            var group = _tasks.Group("free", TaskExecutionModeShared.FreeOrder, t1, t2, t3);

            _bus.Publish(new TaskGroupEventArgs(group));

            _bus.Publish(new ActionAttemptedEvent("action_c"));
            _bus.Publish(new ActionAttemptedEvent("action_a"));
            _bus.Publish(new ActionAttemptedEvent("action_b"));

            Assert.AreEqual(3, _taskCompletions.Count);
            Assert.IsEmpty(_violations);
        }

        [Test]
        public void FreeOrderGroup_RepeatedActionAfterCompletion_IsIgnored()
        {
            var t1 = _tasks.Task("t1", "action_a");
            var group = _tasks.Group("free", TaskExecutionModeShared.FreeOrder, t1);

            _bus.Publish(new TaskGroupEventArgs(group));

            _bus.Publish(new ActionAttemptedEvent("action_a"));
            _bus.Publish(new ActionAttemptedEvent("action_a"));

            Assert.AreEqual(1, _taskCompletions.Count);
            Assert.IsEmpty(_violations);
        }

        [Test]
        public void InjectedPpeChecker_OverridesEventState()
        {
            var engine = new SafetyRuleEngineCore(
                _bus,
                ppeChecker: new AlwaysCompliantChecker());
            engine.Subscribe();

            var task = _tasks.Task("t", "action", PPEType.Helmet);
            var group = _tasks.Group("g", TaskExecutionModeShared.Sequential, task);

            _bus.Publish(new TaskGroupEventArgs(group));
            _bus.Publish(new TaskEventArgs(task));
            _bus.Publish(new ActionAttemptedEvent("action"));

            Assert.IsEmpty(_violations);
            Assert.AreEqual(TaskState.CompletedSuccess,
                _taskCompletions[_taskCompletions.Count - 1].RuntimeTask.State);

            engine.Dispose();
        }

        private sealed class AlwaysCompliantChecker : IPPEComplianceChecker
        {
            public bool IsCompliant(IReadOnlyCollection<PPEType> requiredPpe) => true;
        }
    }
}
