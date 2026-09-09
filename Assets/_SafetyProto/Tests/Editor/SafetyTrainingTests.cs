using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers the production <see cref="EventBus"/> ScriptableObject itself — the queue and its
    /// drain — rather than any system built on top of it.
    ///
    /// Unity does not run <c>Awake</c> or <c>OnEnable</c> on a MonoBehaviour added outside Play
    /// Mode, so an EditMode fixture cannot exercise a host component that wires itself up there
    /// (<c>ScoreManagerAdapter</c> is the one this fixture used to reach for). Those bridges are
    /// covered engine-independently by <c>SessionIntegrationTests</c> through
    /// <c>SessionTestHarness</c>; reaching them through their real MonoBehaviour would need a
    /// PlayMode fixture, which this project does not have.
    /// </summary>
    public class SafetyTrainingTests
    {
        private bool _taskCompleted;
        private bool _safetyViolation;

        [SetUp]
        public void SetUp()
        {
            _taskCompleted = false;
            _safetyViolation = false;

            EventContext.StartSession("test-session", "player", "scene");
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
            EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
            EventBus.Instance.onActionAttempt.RemoveListener(EchoCompletionAndViolation);

            EventContext.Clear();
        }

        /// <summary>
        /// Smoke test for the production EventBus.Instance queue/dispatch plumbing — NOT a
        /// stand-in for SafetyRuleEngineCore (SafetyRuleEngineCoreTests owns that). It confirms
        /// that an ActionAttempt reaches a listener and that events queued from inside that
        /// listener (TaskCompleted, SafetyViolation) are drained and delivered by the same
        /// ProcessEvents() call, across two different payload types.
        /// </summary>
        [Test]
        public void EventBus_DeliversQueuedEventsToListeners_AcrossPayloadTypes()
        {
            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
            EventBus.Instance.onSafetyViolation.AddListener(OnSafetyViolation);
            EventBus.Instance.onActionAttempt.AddListener(EchoCompletionAndViolation);

            SessionEvents.RaiseSessionStarted();
            EventBus.Instance.RaiseTaskStarted(new TaskEventArgs());

            ActionEvents.PublishActionAttempt("test_action");
            ProcessEvents();

            Assert.IsTrue(_taskCompleted, "Expected TaskCompleted to be delivered through the queue.");
            Assert.IsTrue(_safetyViolation, "Expected SafetyViolation to be delivered through the queue.");
        }

        private void EchoCompletionAndViolation(ActionAttemptedEvent _)
        {
            EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs());
            SafetyEvents.RaiseSafetyViolation(new SafetyViolationEventArgs
            {
                ViolationCode = "PPE_MISSING",
                Message = "Simulated missing PPE"
            });
        }

        private void OnTaskCompleted(TaskEventArgs _)
        {
            _taskCompleted = true;
        }

        private void OnSafetyViolation(SafetyViolationEventArgs _)
        {
            _safetyViolation = true;
        }

        private static void ProcessEvents()
        {
            EventBus.Instance.ProcessEvents(10);
        }
    }
}
