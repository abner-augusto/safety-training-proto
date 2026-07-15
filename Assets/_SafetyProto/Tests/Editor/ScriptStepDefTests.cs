using Newtonsoft.Json;
using NUnit.Framework;
using SafetyProto.Domain.Scenarios;

namespace SafetyProto.Tests.Editor
{
    public class ScriptStepDefTests
    {
        [Test]
        public void GateTarget_IsOptionalForExistingScripts()
        {
            var step = JsonConvert.DeserializeObject<ScriptStepDef>(
                "{\"kind\":\"gate\",\"delayMs\":100}");

            Assert.That(step, Is.Not.Null);
            Assert.That(step!.GateTarget, Is.Null);
            Assert.That(step.Kind, Is.EqualTo("gate"));
        }

        [Test]
        public void GateTarget_ReadsPhaseTarget()
        {
            var step = JsonConvert.DeserializeObject<ScriptStepDef>(
                "{\"kind\":\"gate\",\"gateTarget\":\"phase1\"}");

            Assert.That(step!.GateTarget, Is.EqualTo("phase1"));
        }
    }
}
