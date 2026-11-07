#nullable enable
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Domain.Sessions;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// What the evaluator did reaches the session log: who set the participant code, which
    /// interrupted session a restart replaces, and every in-session dashboard intervention.
    /// The analysis counts attempts and separates evaluator help from participant actions.
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
        public void RestartedSession_RecordsSourcePreviousSessionAndAttempt()
        {
            _bus.Publish(new SessionStartedEventArgs
            {
                SessionId = "S2", PlayerId = "P-07", TotalTasks = 9, TimestampMs = 1000L,
                ParticipantSource = ParticipantSources.Dashboard, PreviousSessionId = "S1", Attempt = 2
            });

            var log = WriteLog();
            var started = ((JArray)log["entries"]!)[0];
            Assert.AreEqual("dashboard", (string?)started["data"]!["source"]);
            Assert.AreEqual("S1", (string?)started["data"]!["previousSessionId"]);
            Assert.AreEqual(2, (int?)started["data"]!["attempt"]);

            var summary = log["summary"]!;
            Assert.AreEqual("P-07", (string?)summary["participantId"]);
            Assert.AreEqual("dashboard", (string?)summary["participantSource"]);
            Assert.AreEqual("S1", (string?)summary["previousSessionId"]);
            Assert.AreEqual(2, (int?)summary["attempt"]);
        }

        [Test]
        public void EvaluatorCommand_IsLoggedWithItsSummary()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 9, TimestampMs = 1000L, Attempt = 1 });
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

        [Test]
        public void HighlightCommand_IsLoggedWithItsTarget()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 9, TimestampMs = 1000L, Attempt = 1 });
            _bus.Publish(new EvaluatorCommandEventArgs
            {
                SessionId = "S1", Command = "highlight_target", Target = "ppe:Safety_Helmet",
                Summary = "Capacete destacado por 8 s.", TimestampMs = 7000L
            });

            var entry = ((JArray)WriteLog()["entries"]!).Single(e => (string?)e["eventName"] == "EvaluatorCommand");
            Assert.AreEqual("highlight_target", (string?)entry["data"]!["command"]);
            Assert.AreEqual("ppe:Safety_Helmet", (string?)entry["data"]!["target"]);
            StringAssert.Contains("chamar atenção", (string?)entry["details"]);
        }

        [Test]
        public void GrabsAndReleases_AreLoggedPerObject_TellingDropsFromPlacements()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 9, TimestampMs = 1000L, Attempt = 1 });
            _bus.Publish(new ObjectGrabbedEventArgs { SessionId = "S1", Target = "ToeboardPiece", TimestampMs = 2000L });
            _bus.Publish(new ObjectReleasedEventArgs { SessionId = "S1", Target = "ToeboardPiece", HeldSeconds = 1.5f, Secured = false, TimestampMs = 3500L });
            _bus.Publish(new ObjectGrabbedEventArgs { SessionId = "S1", Target = "ToeboardPiece", TimestampMs = 5000L });
            _bus.Publish(new ObjectReleasedEventArgs { SessionId = "S1", Target = "ToeboardPiece", HeldSeconds = 4f, Secured = true, TimestampMs = 9000L });

            var entries = (JArray)WriteLog()["entries"]!;
            Assert.AreEqual(2, entries.Count(e => (string?)e["eventName"] == "ObjectGrabbed"));
            var releases = entries.Where(e => (string?)e["eventName"] == "ObjectReleased").ToList();
            Assert.AreEqual("ToeboardPiece", (string?)releases[0]["data"]!["target"]);
            Assert.AreEqual(1.5f, (float?)releases[0]["data"]!["durationSeconds"]);
            Assert.IsNull(releases[0]["data"]!["secured"], "a drop omits the default flag");
            Assert.AreEqual(true, (bool?)releases[1]["data"]!["secured"]);
        }

        [Test]
        public void InferredEquip_CarriesItsContext_AnOrdinaryOneDoesNot()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 9, TimestampMs = 1000L, Attempt = 1 });
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Boots, true) { SessionId = "S1", TimestampMs = 2000L });
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, true, "ppe_snap:grip_lost=tracking") { SessionId = "S1", TimestampMs = 3000L });

            var equips = ((JArray)WriteLog()["entries"]!).Where(e => (string?)e["eventName"] == "PpeStateChanged").ToList();
            Assert.IsNull(equips[0]["data"]!["context"]);
            Assert.AreEqual("ppe_snap:grip_lost=tracking", (string?)equips[1]["data"]!["context"]);
        }

        [Test]
        public void Practice_IsLoggedWithHowItEndedAndWhatWasTried()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 9, TimestampMs = 1000L, Attempt = 1 });
            _bus.Publish(new PracticeEventArgs { SessionId = "S1", Phase = PracticePhase.Started, Reason = string.Empty, TimestampMs = 2000L });
            _bus.Publish(new PracticeEventArgs
            {
                SessionId = "S1", Phase = PracticePhase.Ended, Reason = "continue", Seconds = 95f,
                Tried = new[] { "equip_cube", "two_hand_bar" }, TimestampMs = 97000L
            });

            var entries = (JArray)WriteLog()["entries"]!;
            Assert.AreEqual(1, entries.Count(e => (string?)e["eventName"] == "PracticeStarted"));
            var ended = entries.Single(e => (string?)e["eventName"] == "PracticeEnded");
            Assert.AreEqual("continue", (string?)ended["data"]!["reason"]);
            Assert.AreEqual(95f, (float?)ended["data"]!["durationSeconds"]);
            CollectionAssert.AreEqual(new[] { "equip_cube", "two_hand_bar" }, ended["data"]!["tried"]!.Select(t => (string?)t).ToArray());
            StringAssert.Contains("Continuar", (string?)ended["details"]);
        }

        [Test]
        public void HintShown_IsLoggedWithLevelTaskAndWaitingTime()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 9, TimestampMs = 1000L, Attempt = 1 });
            _bus.Publish(new HintShownEventArgs
            {
                SessionId = "S1", Level = 2, TaskId = "install_toeboard", TaskName = "Instalar Rodapé",
                SecondsWithoutProgress = 30f, TimestampMs = 31000L
            });

            var entry = ((JArray)WriteLog()["entries"]!).Single(e => (string?)e["eventName"] == "HintShown");
            Assert.AreEqual(2, (int?)entry["data"]!["level"]);
            Assert.AreEqual("install_toeboard", (string?)entry["data"]!["taskId"]);
            Assert.AreEqual(30f, (float?)entry["data"]!["durationSeconds"]);
        }

        private JObject WriteLog()
        {
            var path = _logger.WriteLogAsync().GetAwaiter().GetResult();
            Assert.IsNotNull(path, "the log was not written");
            return JObject.Parse(File.ReadAllText(path!));
        }
    }
}
