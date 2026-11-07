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
            _relay = new DashboardEventRelay(_bus, _host, () => SessionMode.Guided);
            _relay.Subscribe();
        }

        [Test]
        public void SessionStarted_BroadcastsSessionThenManifest()
        {
            _bus.Publish(new SessionStartedEventArgs { SessionId = "S1", PlayerId = "P-1234", TimestampMs = 42L });
            Assert.AreEqual(2, _host.Broadcasts.Count);
            Assert.AreEqual("SessionStarted", _host.Broadcasts[0].eventType,
                "SessionStarted must broadcast before the manifest.");
            Assert.AreEqual("SessionManifest", _host.Broadcasts[1].eventType);

            var dto = (SessionDto)_host.Broadcasts[0].payload;
            Assert.AreEqual("S1", dto.sessionId);
            Assert.AreEqual("P-1234", dto.participantId);
            Assert.AreEqual(42L, dto.timestampMs);
        }

        [Test]
        public void SessionStarted_InjectedEvaluationMode_BroadcastsAvaliacao()
        {
            var bus = new FakeEventBus();
            var host = new FakeDashboardHost();
            var relay = new DashboardEventRelay(bus, host, () => SessionMode.Evaluation);
            relay.Subscribe();

            bus.Publish(new SessionStartedEventArgs { SessionId = "S1", PlayerId = "P-1", TimestampMs = 1L });

            Assert.AreEqual("avaliacao", host.Last<SessionDto>("SessionStarted").mode);
            relay.Unsubscribe();
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
        public void SessionCompleted_BroadcastsWithoutStartingAFileScan()
        {
            _bus.Publish(new SessionCompletedEventArgs { SessionId = "S1", PlayerId = "P-1", TimestampMs = 9L, totalScore = 500 });
            Assert.AreEqual(500, _host.Last<SessionCompletedDto>("SessionCompleted").totalScore);
        }

        [Test]
        public void Unsubscribe_StopsBroadcasts()
        {
            _relay.Unsubscribe();
            _bus.Publish(new SessionEndedEventArgs { SessionId = "S1", TimestampMs = 1L });
            Assert.AreEqual(0, _host.Count("SessionEnded"));
        }

        [Test]
        public void HintShown_ReachesTheDashboardWithItsLevelAndTask()
        {
            _bus.Publish(new HintShownEventArgs
            {
                SessionId = "S1", Level = 3, TaskId = "install_guardrail", TaskName = "Instalar Guarda-corpo",
                SecondsWithoutProgress = 60f, TimestampMs = 9L
            });

            var dto = _host.Last<HintDto>("HintShown");
            Assert.AreEqual(3, dto.level);
            Assert.AreEqual("install_guardrail", dto.taskId);
            Assert.AreEqual("Instalar Guarda-corpo", dto.taskName);
            Assert.AreEqual(60f, dto.secondsWithoutProgress);
        }

        [Test]
        public void PracticeStarted_ReachesTheDashboardWithoutAnOutcome()
        {
            _bus.Publish(new PracticeEventArgs
            {
                SessionId = "S1", Phase = PracticePhase.Started, Reason = "", TimestampMs = 11L
            });

            Assert.AreEqual(0, _host.Count("PracticeEnded"));
            var dto = _host.Last<PracticeDto>("PracticeStarted");
            Assert.AreEqual("S1", dto.sessionId);
            Assert.AreEqual(11L, dto.timestampMs);
            Assert.AreEqual(string.Empty, dto.reason);
            Assert.AreEqual(0f, dto.seconds);
            Assert.IsNotNull(dto.tried, "the wire carries an empty list, not null, when nothing was tried yet");
            Assert.AreEqual(0, dto.tried.Length);
        }

        [Test]
        public void PracticeEnded_ReachesTheDashboardWithHowItEndedAndWhatWasTried()
        {
            _bus.Publish(new PracticeEventArgs
            {
                SessionId = "S1", Phase = PracticePhase.Ended, Reason = "evaluator", Seconds = 42.5f,
                Tried = new[] { "equip_head", "two_hand_bar" }, TimestampMs = 12L
            });

            Assert.AreEqual(0, _host.Count("PracticeStarted"));
            var dto = _host.Last<PracticeDto>("PracticeEnded");
            Assert.AreEqual("evaluator", dto.reason);
            Assert.AreEqual(42.5f, dto.seconds);
            CollectionAssert.AreEqual(new[] { "equip_head", "two_hand_bar" }, dto.tried);
            Assert.AreEqual(12L, dto.timestampMs);
        }

        [Test]
        public void PracticeEnded_WithoutTriedList_BroadcastsAnEmptyList()
        {
            _bus.Publish(new PracticeEventArgs
            {
                SessionId = "S1", Phase = PracticePhase.Ended, Reason = "timeout", Seconds = 150f, TimestampMs = 13L
            });

            Assert.AreEqual(0, _host.Last<PracticeDto>("PracticeEnded").tried.Length);
        }

        [Test]
        public void Unsubscribe_StopsPracticeBroadcasts()
        {
            _relay.Unsubscribe();
            _bus.Publish(new PracticeEventArgs { SessionId = "S1", Phase = PracticePhase.Started, TimestampMs = 1L });
            Assert.AreEqual(0, _host.Count("PracticeStarted"));
        }
    }
}
