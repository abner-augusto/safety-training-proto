using System;
using System.Collections.Generic;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;

namespace SafetyProto.Tests.Editor
{
    public class EventMetadataTests
    {
        [SetUp]
        public void Setup() => EventContext.StartSession("S1", "P1", "SC1");

        [TearDown]
        public void TearDown() => EventContext.Clear();

        [Test]
        public void AllSupportedPayloadsReceiveTheSameMetadataContract()
        {
            var payloads = new List<object>
            {
                new SessionStartedEventArgs(), new SessionPausedEventArgs(), new SessionResumedEventArgs(),
                new SessionEndedEventArgs(), new SessionCompletedEventArgs(1, 2, 3, 4),
                new ActionAttemptedEvent("action"), new PPEStateChangedEventArgs(PPEType.Helmet, true),
                new TaskEventArgs(null!), new TaskGroupEventArgs(null), new ScoreChangedEventArgs(1, 1),
                new SafetyViolationEventArgs(), new CriticalSafetyFailureEventArgs(), new SafetyErrorEventArgs()
            };

            foreach (var payload in payloads)
            {
                var stamped = Stamp(payload);
                Assert.AreEqual("S1", stamped.SessionId, payload.GetType().Name);
                Assert.AreEqual("P1", stamped.PlayerId, payload.GetType().Name);
                Assert.AreEqual("SC1", stamped.ScenarioId, payload.GetType().Name);
                Assert.Greater(stamped.TimestampMs, 0, payload.GetType().Name);
            }
        }

        [Test]
        public void UnknownMetadataFreePayloadPassesThroughUnchanged()
        {
            const string payload = "unknown";
            Assert.AreEqual(payload, EventMetadata.Stamp(payload));
        }

        private static (string SessionId, string PlayerId, string ScenarioId, long TimestampMs) Stamp(object payload)
        {
            object stamped = payload switch
            {
                SessionStartedEventArgs value => EventMetadata.Stamp(value),
                SessionPausedEventArgs value => EventMetadata.Stamp(value),
                SessionResumedEventArgs value => EventMetadata.Stamp(value),
                SessionEndedEventArgs value => EventMetadata.Stamp(value),
                SessionCompletedEventArgs value => EventMetadata.Stamp(value),
                ActionAttemptedEvent value => EventMetadata.Stamp(value),
                PPEStateChangedEventArgs value => EventMetadata.Stamp(value),
                TaskEventArgs value => EventMetadata.Stamp(value),
                TaskGroupEventArgs value => EventMetadata.Stamp(value),
                ScoreChangedEventArgs value => EventMetadata.Stamp(value),
                SafetyViolationEventArgs value => EventMetadata.Stamp(value),
                CriticalSafetyFailureEventArgs value => EventMetadata.Stamp(value),
                SafetyErrorEventArgs value => EventMetadata.Stamp(value),
                _ => throw new ArgumentException()
            };

            return stamped switch
            {
                SessionStartedEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                SessionPausedEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                SessionResumedEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                SessionEndedEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                SessionCompletedEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                ActionAttemptedEvent value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                PPEStateChangedEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                TaskEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                TaskGroupEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                ScoreChangedEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                SafetyViolationEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                CriticalSafetyFailureEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                SafetyErrorEventArgs value => (value.SessionId, value.PlayerId, value.ScenarioId, value.TimestampMs),
                _ => throw new ArgumentException()
            };
        }
    }
}
