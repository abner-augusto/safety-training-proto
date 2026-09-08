using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime.Task;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class SafetyTrainingTests
    {
        private bool _taskCompleted;
        private bool _safetyViolation;
        private int _score;
        private GameObject _scoreAdapterHost;

        [SetUp]
        public void SetUp()
        {
            _taskCompleted = false;
            _safetyViolation = false;
            _score = 0;

            EventContext.StartSession("test-session", "player", "scene");
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
            EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
            EventBus.Instance.onScoreChanged.RemoveListener(OnScoreChanged);
            EventBus.Instance.onActionAttempt.RemoveListener(EchoCompletionAndViolation);

            if (_scoreAdapterHost != null)
            {
                Object.DestroyImmediate(_scoreAdapterHost);
                _scoreAdapterHost = null;
            }
            ScoreService.DestroyInstance();

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

        /// <summary>
        /// Goes through the production ScoreManagerAdapter (not a test-written score-to-bus
        /// lambda), so this proves the real bridge from ScoreService.ScoreChanged to
        /// ScoreChangedEventArgs on the bus, not just that the bus can dispatch what it is told to.
        /// </summary>
        [Test]
        public void ScoreServiceUpdatesOnScoreChanged()
        {
            EventBus.Instance.onScoreChanged.AddListener(OnScoreChanged);

            _scoreAdapterHost = new GameObject("score-adapter");
            _scoreAdapterHost.AddComponent<ScoreManagerAdapter>();

            ScoreService.Instance.AddPoints(50, "Test points", string.Empty);
            ProcessEvents();

            Assert.AreEqual(50, _score);
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

        private void OnScoreChanged(ScoreChangedEventArgs args)
        {
            _score = args.TotalScore;
        }

        private static void ProcessEvents()
        {
            EventBus.Instance.ProcessEvents(10);
        }
    }
}
