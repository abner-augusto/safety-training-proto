using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class PPEProtocolParticipationTests
    {
        private FakeEventBus _bus;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
        }

        [Test]
        public void Publish_PpeStateChanged_DeliversToTypedSubscriber()
        {
            PPEStateChangedEventArgs received = default;
            int callCount = 0;

            _bus.Subscribe<PPEStateChangedEventArgs>(args =>
            {
                received = args;
                callCount++;
            });

            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet, isWearing: true));

            Assert.AreEqual(1, callCount);
            Assert.AreEqual(PPEType.Helmet, received.PpeType);
            Assert.IsTrue(received.IsWearing);
        }

        [Test]
        public void Publish_PpeStateChanged_TwoSubscribers_InvokedInOrder()
        {
            var order = new System.Collections.Generic.List<string>();

            _bus.Subscribe<PPEStateChangedEventArgs>(_ => order.Add("A"));
            _bus.Subscribe<PPEStateChangedEventArgs>(_ => order.Add("B"));

            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Harness, true));

            Assert.AreEqual(2, order.Count);
            Assert.AreEqual("A", order[0]);
            Assert.AreEqual("B", order[1]);
        }

        [Test]
        public void Unsubscribe_StopsDelivery_ToSpecificHandler()
        {
            int callsA = 0, callsB = 0;

            System.Action<PPEStateChangedEventArgs> handlerA = _ => callsA++;
            System.Action<PPEStateChangedEventArgs> handlerB = _ => callsB++;

            _bus.Subscribe(handlerA);
            _bus.Subscribe(handlerB);

            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Vest, true));
            _bus.Unsubscribe(handlerA);
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Vest, false));

            Assert.AreEqual(1, callsA, "Handler A should have stopped receiving after unsubscribe.");
            Assert.AreEqual(2, callsB, "Handler B should still receive after A unsubscribes.");
        }

        [Test]
        public void ExternalProducer_CanDriveStateTransitions_WithoutUnityScene()
        {
            var compliance = new System.Collections.Generic.Dictionary<PPEType, bool>();
            _bus.Subscribe<PPEStateChangedEventArgs>(args =>
            {
                compliance[args.PpeType] = args.IsWearing;
            });

            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet,    true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Harness,   true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Boots,     true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.GloveLeft, true));

            Assert.AreEqual(4, compliance.Count);
            Assert.IsTrue(compliance[PPEType.Helmet]);
            Assert.IsTrue(compliance[PPEType.Harness]);
            Assert.IsTrue(compliance[PPEType.Boots]);
            Assert.IsTrue(compliance[PPEType.GloveLeft]);
        }

        [Test]
        public void FakeEventBus_Records_PublishChronologically()
        {
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet,  true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Harness, true));
            _bus.Publish(new PPEStateChangedEventArgs(PPEType.Helmet,  false));

            var ppeEvents = _bus.EventsOf<PPEStateChangedEventArgs>();

            Assert.AreEqual(3, ppeEvents.Count);
            Assert.AreEqual(PPEType.Helmet,  ppeEvents[0].PpeType);
            Assert.IsTrue(ppeEvents[0].IsWearing);
            Assert.AreEqual(PPEType.Harness, ppeEvents[1].PpeType);
            Assert.AreEqual(PPEType.Helmet,  ppeEvents[2].PpeType);
            Assert.IsFalse(ppeEvents[2].IsWearing);
        }
    }
}
