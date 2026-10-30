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
    /// Every pause in the session log names what caused it, and overlapping holds can be
    /// rebuilt from the entries. On 28/10 a 5-minute pause with tasks completed inside it could
    /// not be attributed to a popup, the system menu or a removed headset.
    /// </summary>
    public class SessionLogPauseTests
    {
        private FakeEventBus _bus = null!;
        private string _outputDir = string.Empty;
        private SessionLoggerCore _logger = null!;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _outputDir = Path.Combine(Path.GetTempPath(), "safetyproto_pausetests_" + Guid.NewGuid().ToString("N"));
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
        public void PauseAndResume_CarryTheirSourceAndDuration()
        {
            Start(1000L);
            Hold(PauseSource.XrSessionUnfocused, 2000L, paused: true);
            Release(PauseSource.XrSessionUnfocused, 4500L, resumed: true);

            var entries = Entries();
            var paused = entries.Single(e => (string?)e["eventName"] == "SessionPaused");
            Assert.AreEqual("XrSessionUnfocused", (string?)paused["data"]!["pauseSource"]);
            CollectionAssert.AreEqual(new[] { "XrSessionUnfocused" }, paused["data"]!["pauseSources"]!.Select(t => (string?)t));
            StringAssert.Contains("menu do sistema", (string?)paused["details"]);

            var resumed = entries.Single(e => (string?)e["eventName"] == "SessionResumed");
            Assert.AreEqual("XrSessionUnfocused", (string?)resumed["data"]!["pauseSource"]);
            Assert.AreEqual(2.5f, (float)resumed["data"]!["durationSeconds"]!, 0.001f);
        }

        [Test]
        public void OverlappingHolds_AreRebuiltFromHoldChanges()
        {
            Start(1000L);
            Hold(PauseSource.Popup, 2000L, paused: true);
            Hold(PauseSource.ApplicationPause, 3000L, paused: false);
            Release(PauseSource.Popup, 4000L, resumed: false);
            Release(PauseSource.ApplicationPause, 5000L, resumed: true);

            var changes = Entries().Where(e => (string?)e["eventName"] == "PauseHoldChanged").ToList();
            Assert.AreEqual(4, changes.Count);
            CollectionAssert.AreEqual(
                new[] { "Popup:True", "ApplicationPause:True", "Popup:False", "ApplicationPause:False" },
                changes.Select(e => $"{e["data"]!["pauseSource"]}:{(bool?)e["data"]!["held"] ?? false}"));
            CollectionAssert.AreEqual(new[] { "ApplicationPause", "Popup" },
                changes[1]["data"]!["pauseSources"]!.Select(t => (string?)t));

            var resumed = Entries().Single(e => (string?)e["eventName"] == "SessionResumed");
            Assert.AreEqual("ApplicationPause", (string?)resumed["data"]!["pauseSource"]);
            Assert.AreEqual(3f, (float)resumed["data"]!["durationSeconds"]!, 0.001f);
        }

        [Test]
        public void HoldTakenBeforeSessionStarted_IsLoggedAsAPauseInsideTheSession()
        {
            Hold(PauseSource.Popup, 500L, paused: true);
            Start(1000L);
            Release(PauseSource.Popup, 1400L, resumed: true);

            var entries = Entries();
            Assert.AreEqual("SessionStarted", (string?)entries[0]["eventName"]);
            Assert.AreEqual("SessionPaused", (string?)entries[1]["eventName"]);
            Assert.AreEqual("pre_session", (string?)entries[1]["data"]!["context"]);
            Assert.AreEqual("Popup", (string?)entries[1]["data"]!["pauseSource"]);

            var resumed = entries.Single(e => (string?)e["eventName"] == "SessionResumed");
            Assert.AreEqual(0.4f, (float)resumed["data"]!["durationSeconds"]!, 0.001f);
        }

        private void Start(long ms) =>
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", TotalTasks = 1, TimestampMs = ms });

        // Mirrors SessionPause: the hold change is published ahead of the transition it causes.
        private void Hold(PauseSource source, long ms, bool paused)
        {
            var active = ActiveAfter(source, held: true);
            _bus.Publish(new PauseHoldChangedEventArgs { SessionId = "S1", Source = source, Held = true, ActiveSources = active, TimestampMs = ms });
            if (paused)
                _bus.Publish(new SessionPausedEventArgs { SessionId = "S1", Source = source, ActiveSources = active, TimestampMs = ms });
        }

        private void Release(PauseSource source, long ms, bool resumed)
        {
            var active = ActiveAfter(source, held: false);
            _bus.Publish(new PauseHoldChangedEventArgs { SessionId = "S1", Source = source, Held = false, ActiveSources = active, TimestampMs = ms });
            if (resumed)
                _bus.Publish(new SessionResumedEventArgs { SessionId = "S1", Source = source, TimestampMs = ms });
        }

        private readonly PauseHolds _holds = new PauseHolds();

        private PauseSource[] ActiveAfter(PauseSource source, bool held)
        {
            if (held) _holds.Acquire(source);
            else _holds.Release(source);
            return _holds.ActiveSources();
        }

        private JArray Entries()
        {
            var path = _logger.WriteLogAsync().GetAwaiter().GetResult();
            Assert.IsNotNull(path, "the log was not written");
            var log = JObject.Parse(File.ReadAllText(path!));
            return (JArray)log["entries"]!;
        }
    }
}
