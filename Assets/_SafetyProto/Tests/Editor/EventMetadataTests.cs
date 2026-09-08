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
                new ActionAttemptedEvent("action"),
                new ActionRefusedEventArgs("action", "source", "PREREQUISITE_PENDING"),
                new PopupClosedEventArgs(),
                new PPEStateChangedEventArgs(PPEType.Helmet, true),
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

        /// <summary>
        /// A single <see cref="EventMetadata.Stamp{T}"/> call with <c>T = object</c> — the
        /// runtime type patterns inside <c>Stamp</c> match on the boxed value's ACTUAL type
        /// regardless of the static type parameter, so this does not need (and must not
        /// maintain) a second copy of the same per-type case list that <c>EventMetadata.cs</c>
        /// already owns. Reading the four stamped fields back uses reflection instead of a
        /// second switch for the same reason: every payload type declares public
        /// SessionId/PlayerId/ScenarioId/TimestampMs fields, but with no shared interface, so
        /// reflection is the only way to read them generically without re-listing every type.
        /// </summary>
        private static (string SessionId, string PlayerId, string ScenarioId, long TimestampMs) Stamp(object payload)
        {
            object stamped = EventMetadata.Stamp(payload)!;
            var type = stamped.GetType();

            string GetString(string field) => (string)type.GetField(field)!.GetValue(stamped)!;
            long GetTimestamp() => (long)type.GetField("TimestampMs")!.GetValue(stamped)!;

            return (GetString("SessionId"), GetString("PlayerId"), GetString("ScenarioId"), GetTimestamp());
        }
    }
}
