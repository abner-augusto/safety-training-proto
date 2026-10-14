using System.Reflection;
using NUnit.Framework;
using SafetyProto.Runtime;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class DashboardCommandRoutingTests
    {
        private GameObject _root;
        private RecenterService _service;
        private RecenterCommandHandler _handler;
        private FakeAnchorProvider _anchorProvider;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("DashboardCommandRoutingTestRoot");
            _service = _root.AddComponent<RecenterService>();
            _anchorProvider = _root.AddComponent<FakeAnchorProvider>();
            _handler = _root.AddComponent<RecenterCommandHandler>();

            SetPrivateField(_handler, "recenterService", _service);
            SetPrivateField(_handler, "anchorProviderBehaviour", _anchorProvider);
            InvokePrivate(_handler, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void TryExecute_NullAnchor_RejectsWithPortugueseReason()
        {
            _anchorProvider.Anchor = null;

            bool accepted = _handler.TryExecute(out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual("Âncora da fase não configurada.", reason);
        }

        [Test]
        public void TryExecute_ServiceBusy_RejectsWithPortugueseReason()
        {
            GameObject anchorGo = null;
            try
            {
                anchorGo = new GameObject("Anchor");
                _anchorProvider.Anchor = anchorGo.transform;

                // With no OVRScreenFade the sequence runs synchronously to its first yield, which sets IsBusy.
                var busySequence = _service.RecenterTo(_anchorProvider.Anchor, new RecenterOptions
                {
                    HoldBlackDuration = 999f,
                });
                busySequence.MoveNext();
                Assert.IsTrue(_service.IsBusy, "test setup expected the service to already be busy");

                bool accepted = _handler.TryExecute(out var reason);

                Assert.IsFalse(accepted);
                Assert.AreEqual("Transição em andamento.", reason);
            }
            finally
            {
                if (anchorGo != null) Object.DestroyImmediate(anchorGo);
            }
        }

        [Test]
        public void TryExecute_AnchorPresentAndServiceFree_Accepts()
        {
            GameObject anchorGo = null;
            try
            {
                anchorGo = new GameObject("Anchor");
                _anchorProvider.Anchor = anchorGo.transform;

                bool accepted = _handler.TryExecute(out var reason);

                Assert.IsTrue(accepted);
                Assert.AreEqual(string.Empty, reason);
            }
            finally
            {
                if (anchorGo != null) Object.DestroyImmediate(anchorGo);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"Expected private field '{fieldName}' on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, $"Expected private method '{methodName}' on {target.GetType().Name}");
            method.Invoke(target, null);
        }

        private class FakeAnchorProvider : MonoBehaviour, IRecenterAnchorProvider
        {
            public Transform Anchor;
            public Transform CurrentAnchor => Anchor;
        }
    }
}
