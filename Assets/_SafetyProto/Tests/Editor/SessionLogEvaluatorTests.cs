#nullable enable
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Domain.Sessions;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Every in-session dashboard intervention reaches the session log, so the analysis can
    /// separate evaluator help from what the participant did.
    /// </summary>
    public class SessionLogEvaluatorTests
    {
        private FakeEventBus _bus = null!;
        private string _outputDir = string.Empty;
        private SessionLoggerCore _logger = null!;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _outputDir = Path.Combine(Path.GetTempPath(), "safetyproto_evaltests_" + Guid.NewGuid().ToString("N"));
            _logger = new SessionLoggerCore(
                _bus, _outputDir, SessionLoggerCore.SerializeIndentedOmittingDefaults, () => SessionMode.Guided);
            _logger.Subscribe();
        }

        [TearDown]
        public void TearDown()
        {
            _logger.Dispose();
            if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true);
        }

        [Test]
        public void EvaluatorCommand_IsLoggedWithItsSummary()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 9, TimestampMs = 1000L });
            _bus.Publish(new EvaluatorCommandEventArgs
            {
                SessionId = "S1", Command = "return_all_ppe", Summary = "2 EPIs devolvidos à posição inicial.", TimestampMs = 5000L
            });

            var entry = ((JArray)WriteLog()["entries"]!).Single(e => (string?)e["eventName"] == "EvaluatorCommand");
            Assert.AreEqual("return_all_ppe", (string?)entry["data"]!["command"]);
            Assert.AreEqual(5000L, (long?)entry["timestampMs"]);
            StringAssert.Contains("recolher objetos soltos", (string?)entry["details"]);
            StringAssert.Contains("2 EPIs", (string?)entry["details"]);
        }

        private JObject WriteLog()
        {
            var path = _logger.WriteLogAsync().GetAwaiter().GetResult();
            Assert.IsNotNull(path, "the log was not written");
            return JObject.Parse(File.ReadAllText(path!));
        }
    }
}
