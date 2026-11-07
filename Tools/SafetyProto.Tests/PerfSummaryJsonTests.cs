using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SafetyProto.Domain.Metrics;

namespace SafetyProto.Tests
{
    public class PerfSummaryJsonTests
    {
        [Test]
        public void MeasuredSummaryPreservesSessionLogFieldOrder()
        {
            var summary = new PerfSummary { mainThreadMedianMs = 10, mainThreadP95Ms = 19 };
            var json = JObject.Parse(JsonConvert.SerializeObject(summary));

            CollectionAssert.AreEqual(new[]
            {
                "framesRecorded", "framesWithFlush", "totalEvents", "budgetCutFrames",
                "flushMedianUs", "flushP95Us", "flushP99Us", "flushMaxUs",
                "frameTimingAvailable", "mainThreadMedianMs", "mainThreadP95Ms",
                "truncated", "percentileMethod"
            }, json.Properties().Select(property => property.Name));
            Assert.AreEqual(10d, (double)json["mainThreadMedianMs"]!);
            Assert.AreEqual(19d, (double)json["mainThreadP95Ms"]!);
        }
    }
}
