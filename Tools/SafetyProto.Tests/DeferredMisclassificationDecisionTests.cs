using NUnit.Framework;
using SafetyProto.Domain.Safety;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests
{
    public class DeferredMisclassificationDecisionTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void WrongChoice_IsDueImmediatelyOnlyWhenTaskAlreadyFinished(bool alreadyFinished)
        {
            var task = new FakeTaskBuilder().Task("Report hazard", "report_hazard");
            var deferred = new DeferredMisclassification();

            var due = deferred.Hold(task, alreadyFinished);

            if (alreadyFinished)
            {
                Assert.AreSame(task, due);
                Assert.IsFalse(deferred.IsPending);
                Assert.IsNull(deferred.TakeOnCompleted(task));
            }
            else
            {
                Assert.IsNull(due);
                Assert.IsTrue(deferred.IsPending);
                Assert.AreSame(task, deferred.TakeOnCompleted(task));
                Assert.IsNull(deferred.TakeOnCompleted(task));
            }
        }
    }
}
