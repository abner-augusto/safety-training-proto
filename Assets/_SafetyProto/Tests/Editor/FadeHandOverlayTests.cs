using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers the culling-mask rule: the hand layers join the overlay camera only while the
    /// screen fade is active or a popup is visible, and leave it again otherwise. The mask is
    /// the whole mechanism, so asserting it directly replaces rendering and reading pixels back.
    /// </summary>
    public class FadeHandOverlayTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private Camera _overlay;
        private OVRScreenFade _fade;
        private FadeHandOverlay _router;
        private int _handMask;
        private int _uiMask;

        [SetUp]
        public void SetUp()
        {
            _handMask = LayerMask.GetMask("Hands");
            _uiMask = LayerMask.GetMask("PopupUI");
            Assert.That(_handMask, Is.Not.Zero);
            _overlay = Track(new GameObject("Overlay")).AddComponent<Camera>();
            _overlay.enabled = false;
            _overlay.cullingMask = _handMask | _uiMask;
            _fade = Track(new GameObject("World")).AddComponent<OVRScreenFade>();
            _fade.fadeOnStart = false;
            _router = _overlay.gameObject.AddComponent<FadeHandOverlay>();
            SetField("screenFade", _fade);
            SetField("handLayers", (LayerMask)_handMask);
            typeof(FadeHandOverlay).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_router, null);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [TestCase(0f, false)]
        [TestCase(0.5f, true)]
        [TestCase(1f, true)]
        public void HandsAreOverlaidOnlyDuringFade(float alpha, bool expectHand)
        {
            _fade.SetExplicitFade(alpha);
            UpdateMask();
            AssertHands(expectHand);
            Assert.That(_overlay.cullingMask & _uiMask, Is.EqualTo(_uiMask));
        }

        [Test]
        public void FadeEnds_RemovesHands()
        {
            _fade.SetExplicitFade(1f);
            UpdateMask();
            AssertHands(true);
            _fade.SetExplicitFade(0f);
            UpdateMask();
            AssertHands(false);
        }

        [Test]
        public void DisabledFade_RemovesHands()
        {
            _fade.SetExplicitFade(1f);
            UpdateMask();
            _fade.enabled = false;
            UpdateMask();
            AssertHands(false);
        }

        [Test]
        public void PopupVisible_OverlaysHandsWithoutFade()
        {
            PublishAndDispatch(new PopupShownEventArgs());
            UpdateMask();
            AssertHands(true);
        }

        [Test]
        public void PopupClosed_RemovesHands()
        {
            PublishAndDispatch(new PopupShownEventArgs());
            UpdateMask();
            AssertHands(true);

            PublishAndDispatch(new PopupClosedEventArgs());
            UpdateMask();
            AssertHands(false);
        }

        [Test]
        public void PopupClosesDuringFade_HandsStayOverlaid()
        {
            _fade.SetExplicitFade(1f);
            PublishAndDispatch(new PopupShownEventArgs());
            PublishAndDispatch(new PopupClosedEventArgs());
            UpdateMask();
            AssertHands(true);
        }

        private static void PublishAndDispatch<T>(T payload)
        {
            EventBus.Instance.Publish(payload);
            EventBus.Instance.ProcessEvents(10);
        }

        private void UpdateMask()
        {
            typeof(FadeHandOverlay).GetMethod("OnPreCull", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_router, null);
        }

        private void SetField(string field, object value)
        {
            typeof(FadeHandOverlay).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_router, value);
        }

        private void AssertHands(bool expectHand)
        {
            Assert.That(_overlay.cullingMask & _handMask, Is.EqualTo(expectHand ? _handMask : 0),
                $"cullingMask={_overlay.cullingMask}");
        }

        private T Track<T>(T value) where T : Object
        {
            _objects.Add(value);
            return value;
        }
    }
}
