// Assets/_SafetyProto/Tests/Editor/DashboardEventRelayTests.cs
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class DashboardEventRelayTests
    {
        private FakeEventBus _bus = null!;
        private FakeDashboardHost _host = null!;
        private DashboardEventRelay _relay = null!;
        private FakeTaskBuilder _builder = null!;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _host = new FakeDashboardHost();
            _builder = new FakeTaskBuilder();
            _relay = new DashboardEventRelay(_bus, _host);
            _relay.Subscribe();
        }

        [Test]
        public void SessionStarted_BroadcastsSessionThenManifest()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", PlayerId = "P-1234", TimestampMs = 42L });

            var dto = _host.Last<SessionDto>("SessionStarted");
            Assert.AreEqual("S1", dto.sessionId);
            Assert.AreEqual("P-1234", dto.participantId);
            Assert.AreEqual(42L, dto.timestampMs);
            Assert.AreEqual(1, _host.Count("SessionManifest"));
        }

        [Test]
        public void SessionStarted_EmptyPlayer_UsesDashPlaceholder()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", PlayerId = "", TimestampMs = 1L });
            Assert.AreEqual("—", _host.Last<SessionDto>("SessionStarted").participantId);
        }

        [Test]
        public void TaskEvents_DemuxByPhase_ToWireTypeAndStatus()
        {
            var task = _builder.Task("t1", "a1");
            var group = _builder.Group("G", TaskExecutionModeShared.Sequential, task);
            _host.SeedKnownGroup(group);

            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Started) { SessionId = "S1", TimestampMs = 10L });
            _bus.Publish(new TaskEventArgs(task, null, TaskPhase.Completed) { SessionId = "S1", TimestampMs = 20L });

            Assert.AreEqual("active", _host.Last<TaskDto>("TaskStarted").status);
            Assert.AreEqual("completed", _host.Last<TaskDto>("TaskCompleted").status);
            Assert.AreEqual(20L, _host.Last<TaskDto>("TaskCompleted").timestampMs);
        }

        [Test]
        public void GroupStarted_RegistersGroupAndBroadcastsManifest_CompletedDoesNotRegister()
        {
            var group = _builder.Group("G", TaskExecutionModeShared.Sequential);

            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Started) { SessionId = "S1", TimestampMs = 5L });
            Assert.AreEqual(1, _host.Registered.Count);
            Assert.AreEqual("GroupStarted", _host.Broadcasts[_host.Broadcasts.Count - 2].eventType);
            Assert.AreEqual("SessionManifest", _host.Broadcasts[_host.Broadcasts.Count - 1].eventType);

            _host.Registered.Clear();
            _bus.Publish(new TaskGroupEventArgs(group, TaskGroupPhase.Completed) { SessionId = "S1", TimestampMs = 6L });
            Assert.AreEqual(0, _host.Registered.Count, "GroupCompleted must not register the group");
            Assert.AreEqual("GroupCompleted", _host.Broadcasts[_host.Broadcasts.Count - 2].eventType);
        }

        [Test]
        public void PpeAndActionAttempt_AreGatedByVerboseEvents()
        {
            _host.VerboseEvents = false;
            _bus.Publish(new PPEStateChangedEventArgs { SessionId = "S1", PpeType = PPEType.Helmet, IsWearing = true, TimestampMs = 1L });
            _bus.Publish(new ActionAttemptedEvent { SessionId = "S1", ActionId = "a", TimestampMs = 1L });
            Assert.AreEqual(0, _host.Count("PpeChanged"));
            Assert.AreEqual(0, _host.Count("ActionAttempt"));

            _host.VerboseEvents = true;
            _bus.Publish(new PPEStateChangedEventArgs { SessionId = "S1", PpeType = PPEType.Helmet, IsWearing = true, TimestampMs = 1L });
            Assert.AreEqual(1, _host.Count("PpeChanged"));
        }

        [Test]
        public void SessionCompleted_BroadcastsAndTriggersLog()
        {
            _bus.Publish(new SessionCompletedEventArgs { SessionId = "S1", PlayerId = "P-1", TimestampMs = 9L, totalScore = 500 });
            Assert.AreEqual(500, _host.Last<SessionCompletedDto>("SessionCompleted").totalScore);
            Assert.AreEqual(1, _host.LogBroadcasts.Count);
            Assert.AreEqual(("S1", "P-1"), _host.LogBroadcasts[0]);
        }

        [Test]
        public void Unsubscribe_StopsBroadcasts()
        {
            _relay.Unsubscribe();
            _bus.Publish(new SessionEndedEventArgs { SessionId = "S1", TimestampMs = 1L });
            Assert.AreEqual(0, _host.Count("SessionEnded"));
        }
    }
}
