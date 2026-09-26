using System.Reflection;
using NUnit.Framework;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class HandSurfaceContactAudioTests
    {
        private GameObject _hand;
        private HandSurfaceContactAudio _audio;

        [SetUp]
        public void SetUp()
        {
            _hand = new GameObject("Contact audio test");
            _hand.SetActive(false);
            _audio = _hand.AddComponent<HandSurfaceContactAudio>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_hand);

        [Test]
        public void SustainedContact_PlaysOnlyOnEntry()
        {
            Assert.That(Touch(0f), Is.True);
            for (int i = 1; i <= 100; i++) Assert.That(Touch(i * 0.01f), Is.False);
        }

        [Test]
        public void BriefTrackingGap_DoesNotReplay()
        {
            Assert.That(Touch(0f), Is.True);
            Assert.That(Touch(0.1f), Is.False);
            Assert.That(Touch(0.2f), Is.False);
            Assert.That(Touch(0.3f), Is.False);
        }

        [Test]
        public void SeparatedContact_PlaysAgainAfterCooldown()
        {
            Assert.That(Touch(0f), Is.True);
            Assert.That(Touch(0.3f), Is.True);
        }

        [Test]
        public void ContactDuringCooldown_DoesNotPlayLateWhileHeld()
        {
            Assert.That(Touch(0f), Is.True);
            Assert.That(Touch(0.15f), Is.False);
            Assert.That(Touch(0.21f), Is.False);
            Assert.That(Touch(0.4f), Is.True);
        }

        private bool Touch(float now) => (bool)typeof(HandSurfaceContactAudio)
            .GetMethod("TryBeginContact", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_audio, new object[] { now });
    }
}
