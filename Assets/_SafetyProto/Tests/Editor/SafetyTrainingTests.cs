using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Events;

namespace SafetyProto.Tests.Editor
{
    public class SafetyTrainingTests
    {
        private bool _taskCompleted;
        private bool _safetyViolation;
        private int _score;

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
            EventBus.Instance.onActionAttempt.RemoveListener(SimulateRuleEngine);
            EventBus.Instance.onActionAttempt.RemoveListener(SimulateViolationRule);
            EventContext.Clear();
        }

        [Test]
        public void TaskCompletesWhenCorrectActionAndPpe()
        {
            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
            EventBus.Instance.onActionAttempt.AddListener(SimulateRuleEngine);

            SessionEvents.RaiseSessionStarted();
            TaskEvents.RaiseTaskStarted(new TaskEventArgs());

            ActionEvents.PublishActionAttempt("test_action");
            ProcessEvents();

            Assert.IsTrue(_taskCompleted, "Expected TaskCompleted event to fire via simulated rule engine.");
        }

        [Test]
        public void SafetyViolationRaisedWhenRulesBroken()
        {
            EventBus.Instance.onSafetyViolation.AddListener(OnSafetyViolation);
            EventBus.Instance.onActionAttempt.AddListener(SimulateViolationRule);

            SessionEvents.RaiseSessionStarted();
            TaskEvents.RaiseTaskStarted(new TaskEventArgs());

            ActionEvents.PublishActionAttempt("test_action");
            ProcessEvents();

            Assert.IsTrue(_safetyViolation, "Expected SafetyViolation event when PPE is missing.");
        }

        [Test]
        public void ScoreServiceUpdatesOnScoreChanged()
        {
            EventBus.Instance.onScoreChanged.AddListener(OnScoreChanged);
            var scoreService = new ScoreService();

            scoreService.ScoreChanged += (newScore, delta, reason) =>
            {
                ScoreEvents.RaiseScoreChanged(new ScoreChangedEventArgs(newScore, delta));
            };

            scoreService.AddPoints(50, "Test points");
            ProcessEvents();

            Assert.AreEqual(50, _score);
        }

        private void SimulateRuleEngine(ActionAttemptedEvent _)
        {
            TaskEvents.RaiseTaskCompleted(new TaskEventArgs());
        }

        private void SimulateViolationRule(ActionAttemptedEvent _)
        {
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
