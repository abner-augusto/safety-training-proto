using NUnit.Framework;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.Tests.Editor
{
    public class ScenarioCompatibilityTests
    {
        [Test]
        public void Validate_AcceptsCandidateWithLoadedTaskIdsAndActions()
        {
            var loaded = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[{\"id\":\"t1\",\"name\":\"t1\",\"actionId\":\"action_a\"}]}]}");
            var candidate = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[{\"id\":\"t1\",\"name\":\"t1\",\"actionId\":\"action_a\"}]}],\"script\":[{\"kind\":\"action\",\"actionId\":\"action_a\"}]}");

            var result = ScenarioCompatibility.Validate(candidate.Scenario!, loaded.Scenario!);

            Assert.That(result.Compatible, Is.True, result.ErrorSummary);
        }

        [Test]
        public void Validate_RejectsUnknownScriptAction()
        {
            var loaded = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[{\"id\":\"t1\",\"name\":\"t1\",\"actionId\":\"action_a\"}]}]}");
            var candidate = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[{\"id\":\"t1\",\"name\":\"t1\",\"actionId\":\"action_a\"}]}],\"script\":[{\"kind\":\"action\",\"actionId\":\"missing_action\"}]}");

            var result = ScenarioCompatibility.Validate(candidate.Scenario!, loaded.Scenario!);

            Assert.That(result.Compatible, Is.False);
            StringAssert.Contains("missing_action", result.ErrorSummary);
        }

        [Test]
        public void Validate_RejectsDifferentTaskSemantics()
        {
            var loaded = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"executionMode\":\"Sequential\",\"tasks\":[{\"id\":\"t1\",\"name\":\"t1\",\"actionId\":\"action_a\",\"severity\":\"critical\",\"requiredPPE\":[\"Helmet\"]}]}]}");
            var candidate = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"executionMode\":\"FreeOrder\",\"tasks\":[{\"id\":\"t1\",\"name\":\"t1\",\"actionId\":\"action_b\",\"severity\":\"minor\",\"requiredPPE\":[\"Boots\"]}]}]}");

            var result = ScenarioCompatibility.Validate(candidate.Scenario!, loaded.Scenario!);

            Assert.That(result.Compatible, Is.False);
            StringAssert.Contains("modo de execução", result.ErrorSummary);
            StringAssert.Contains("ação da tarefa", result.ErrorSummary);
            StringAssert.Contains("nível de risco", result.ErrorSummary);
            StringAssert.Contains("EPIs", result.ErrorSummary);
        }

        [Test]
        public void Validate_RejectsDifferentSequentialTaskOrder()
        {
            var loaded = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[{\"id\":\"t1\",\"name\":\"t1\"},{\"id\":\"t2\",\"name\":\"t2\"}]}]}");
            var candidate = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g1\",\"name\":\"g1\",\"tasks\":[{\"id\":\"t2\",\"name\":\"t2\"},{\"id\":\"t1\",\"name\":\"t1\"}]}]}");

            var result = ScenarioCompatibility.Validate(candidate.Scenario!, loaded.Scenario!);

            Assert.That(result.Compatible, Is.False);
            StringAssert.Contains("ordem das tarefas", result.ErrorSummary);
        }
    }
}
