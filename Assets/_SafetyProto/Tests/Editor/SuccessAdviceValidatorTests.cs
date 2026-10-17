using System.Linq;
using NUnit.Framework;
using SafetyProto.Domain.Capabilities;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.Tests.Editor
{
    public class SuccessAdviceValidatorTests
    {
        private static readonly CapabilityCatalog Catalog = new()
        {
            ActionIds = new System.Collections.Generic.List<string> { "inspect_harness" },
            PpeTypes = new System.Collections.Generic.List<string> { "Harness" }
        };

        [Test]
        public void NarratedAdviceRequiresNonEmptyAdvice()
        {
            var load = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g\",\"name\":\"g\",\"tasks\":[" +
                "{\"id\":\"inspect_harness\",\"name\":\"Inspecionar\",\"actionId\":\"inspect_harness\"," +
                "\"successAdviceNarrated\":true}]}]}");
            Assert.That(load.Success, Is.True, load.ErrorSummary);

            var errors = ScenarioValidator.Validate(load.Scenario!, Catalog);

            Assert.That(errors.Any(e => e.Contains("successAdvice")), Is.True, string.Join(" | ", errors));
        }

        [Test]
        public void NarratedAdviceAcceptsNonEmptyAdvice()
        {
            var load = ScenarioLoader.Parse(
                "{\"groups\":[{\"id\":\"g\",\"name\":\"g\",\"tasks\":[" +
                "{\"id\":\"inspect_harness\",\"name\":\"Inspecionar\",\"actionId\":\"inspect_harness\"," +
                "\"successAdvice\":\"Dica.\",\"successAdviceNarrated\":true}]}]}");
            Assert.That(load.Success, Is.True, load.ErrorSummary);

            var errors = ScenarioValidator.Validate(load.Scenario!, Catalog);

            Assert.That(errors.Any(e => e.Contains("successAdvice")), Is.False, string.Join(" | ", errors));
        }
    }
}
