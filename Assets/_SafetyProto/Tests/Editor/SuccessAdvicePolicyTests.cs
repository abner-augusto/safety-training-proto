using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Domain.Feedback;

namespace SafetyProto.Tests.Editor
{
    public class SuccessAdvicePolicyTests
    {
        private const string Advice = "Dica.";

        [Test]
        public void ShowsAdviceForGuidedCorrectCompletion()
        {
            Assert.IsTrue(SuccessAdvicePolicy.ShouldShow(
                TaskPhase.Completed, TaskState.CompletedSuccess, false, SessionMode.Guided, Advice));
        }

        [TestCase(TaskPhase.Started, TaskState.CompletedSuccess, false, SessionMode.Guided, Advice)]
        [TestCase(TaskPhase.Completed, TaskState.CompletedSuccessButUnsafe, false, SessionMode.Guided, Advice)]
        [TestCase(TaskPhase.Completed, TaskState.CompletedSuccess, true, SessionMode.Guided, Advice)]
        [TestCase(TaskPhase.Completed, TaskState.CompletedSuccess, false, SessionMode.Evaluation, Advice)]
        [TestCase(TaskPhase.Completed, TaskState.CompletedSuccess, false, SessionMode.Guided, "")]
        [TestCase(TaskPhase.Completed, TaskState.CompletedSuccess, false, SessionMode.Guided, "  ")]
        public void HidesAdviceWhenAnyRevealConditionFails(
            TaskPhase phase, TaskState outcome, bool misclassified, SessionMode mode, string advice)
        {
            Assert.IsFalse(SuccessAdvicePolicy.ShouldShow(phase, outcome, misclassified, mode, advice));
        }
    }
}
