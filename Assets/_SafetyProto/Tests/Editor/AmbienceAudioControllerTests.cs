using System.Reflection;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class AmbienceAudioControllerTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _host = null!;
        private AmbienceAudioController _controller = null!;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Test_AmbienceAudioController");
            _controller = _host.AddComponent<AmbienceAudioController>();
            AssignDestroyedAudioSource("groundAudioSource");
            AssignDestroyedAudioSource("heightAudioSource");
            AssignDestroyedAudioSource("generatorAudioSource");
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void SessionPauseAndResume_DestroyedAudioSources_DoNotThrow()
        {
            Assert.DoesNotThrow(() => InvokeHandler("OnSessionPaused", new SessionPausedEventArgs()));
            Assert.DoesNotThrow(() => InvokeHandler("OnSessionResumed", new SessionResumedEventArgs()));
        }

        [Test]
        public void ResetSession_DestroyedAudioSources_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.ResetSession());
        }

        private void AssignDestroyedAudioSource(string fieldName)
        {
            var sourceObject = new GameObject($"Destroyed_{fieldName}");
            var source = sourceObject.AddComponent<AudioSource>();
            typeof(AmbienceAudioController).GetField(fieldName, PrivateInstance)!.SetValue(_controller, source);
            Object.DestroyImmediate(sourceObject);
        }

        private void InvokeHandler<T>(string methodName, T eventArgs)
        {
            typeof(AmbienceAudioController).GetMethod(methodName, PrivateInstance)!.Invoke(_controller, new object[] { eventArgs! });
        }
    }
}
