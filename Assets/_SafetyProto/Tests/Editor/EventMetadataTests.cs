using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        /// <summary>
        /// Stamps every payload through a single <see cref="EventMetadata.Stamp{T}"/> call with
        /// <c>T = object</c> and reads the fields back through <see cref="IEventPayload"/>: the
        /// interface is the contract, so the test needs no per-type list of its own beyond the
        /// payload instances.
        /// </summary>
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
                new PopupShownEventArgs(),
                new PPEStateChangedEventArgs(PPEType.Helmet, true),
                new TaskEventArgs(null!), new TaskGroupEventArgs(null), new ScoreChangedEventArgs(1, 1),
                new SafetyViolationEventArgs(), new CriticalSafetyFailureEventArgs(), new SafetyErrorEventArgs()
            };

            foreach (var payload in payloads)
            {
                var stamped = (IEventPayload)EventMetadata.Stamp(payload)!;
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

        [Test]
        public void EveryPayloadStructImplementsIEventPayload()
        {
            var assembly = typeof(EventMetadata).Assembly;
            var candidates = assembly.GetTypes()
                .Where(t => t.IsValueType && !t.IsEnum
                    && t.GetField("SessionId", BindingFlags.Public | BindingFlags.Instance) != null)
                .ToList();

            Assert.GreaterOrEqual(candidates.Count, 16);
            foreach (var t in candidates)
            {
                Assert.IsTrue(typeof(IEventPayload).IsAssignableFrom(t),
                    t.Name + " carries SessionId but does not implement IEventPayload");
            }
        }

        [Test]
        public void StampReturnsACopyWithAllFourFieldsSet()
        {
            var original = new SafetyErrorEventArgs();

            var stamped = EventMetadata.Stamp(original);

            Assert.AreEqual("S1", stamped.SessionId);
            Assert.AreEqual("P1", stamped.PlayerId);
            Assert.AreEqual("SC1", stamped.ScenarioId);
            Assert.Greater(stamped.TimestampMs, 0);
            Assert.IsNull(original.SessionId);
            Assert.AreEqual(0L, original.TimestampMs);
        }
    }
}
