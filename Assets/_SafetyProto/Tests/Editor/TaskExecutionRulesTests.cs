using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Tasks;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class TaskExecutionRulesTests
    {
        [Test]
        public void EvaluationAlwaysUsesFreeOrder()
        {
            var previous = SessionModeState.Current;
            try
            {
                SessionModeState.Current = SessionMode.Evaluation;
                var group = new FakeTaskBuilder().Group("g", TaskExecutionModeShared.Sequential);
                Assert.AreEqual(TaskExecutionModeShared.FreeOrder, TaskExecutionRules.EffectiveMode(group));
            }
            finally { SessionModeState.Current = previous; }
        }

        [Test]
        public void EquipTaskRequiresOnlyPpeSet()
        {
            var builder = new FakeTaskBuilder();
            var equip = builder.Task("Botina", string.Empty, PPEType.Boots);
            var action = builder.Task("Ação", "do_action", PPEType.Boots);
            Assert.IsTrue(TaskExecutionRules.IsEquipTask(equip));
            Assert.IsFalse(TaskExecutionRules.IsEquipTask(action));
        }

        [Test]
        public void ActionMatchingTrimsAndIgnoresCase()
        {
            var task = new FakeTaskBuilder().Task("T", "connect_harness");
            Assert.IsTrue(TaskExecutionRules.MatchesAction(task, "  CONNECT_HARNESS "));
            Assert.IsFalse(TaskExecutionRules.MatchesAction(task, "other"));
        }
    }
}
