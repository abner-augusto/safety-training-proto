// Assets/_SafetyProto/Tests/Editor/AudioFeedbackManagerTests.cs
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Domain.Feedback;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class AudioFeedbackManagerTests
    {
        private GameObject _host = null!;
        private AudioFeedbackManager _manager = null!;
        private AudioClip _successClip = null!;
        private AudioClip _unsafeSuccessClip = null!;
        private AudioClip _failureClip = null!;
        private AudioClip _criticalClip = null!;
        private AudioClip _genericClip = null!;
        private SessionMode _previousMode;

        [SetUp]
        public void SetUp()
        {
            _previousMode = SessionModeState.Current;

            _host = new GameObject("Test_AudioFeedbackManager");
            _host.AddComponent<AudioSource>();
            _manager = _host.AddComponent<AudioFeedbackManager>();

            _successClip = AudioClip.Create("Success", 4410, 1, 44100, false);
            _unsafeSuccessClip = AudioClip.Create("UnsafeSuccess", 4410, 1, 44100, false);
            _failureClip = AudioClip.Create("Failure", 4410, 1, 44100, false);
            _criticalClip = AudioClip.Create("Critical", 4410, 1, 44100, false);
            _genericClip = AudioClip.Create("Generic", 4410, 1, 44100, false);

            _manager.ConfigureClips(_successClip, _unsafeSuccessClip, _failureClip, _criticalClip);
            _manager.SubscribeEvents();
        }

        [TearDown]
        public void TearDown()
        {
            if (_manager != null)
            {
                _manager.UnsubscribeEvents();
            }

            SessionModeState.Current = _previousMode;

            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            if (_successClip != null) Object.DestroyImmediate(_successClip);
            if (_unsafeSuccessClip != null) Object.DestroyImmediate(_unsafeSuccessClip);
            if (_failureClip != null) Object.DestroyImmediate(_failureClip);
            if (_criticalClip != null) Object.DestroyImmediate(_criticalClip);
            if (_genericClip != null) Object.DestroyImmediate(_genericClip);
        }

        private static void ProcessEvents()
        {
            EventBus.Instance.ProcessEvents(10);
        }

        [Test]
        public void GuidedMode_CompliantTaskCompleted_RequestsSuccess()
        {
            SessionModeState.Current = SessionMode.Guided;

            EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs { WasPpeCompliant = true });
            ProcessEvents();

            Assert.IsTrue(_manager.Arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Success, kind);
        }

        [Test]
        public void GuidedMode_UnsafeTaskCompleted_RequestsUnsafeSuccess()
        {
            SessionModeState.Current = SessionMode.Guided;

            EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs { WasPpeCompliant = false });
            ProcessEvents();

            Assert.IsTrue(_manager.Arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.UnsafeSuccess, kind);
        }

        [Test]
        public void GuidedMode_SafetyViolation_RequestsFailure()
        {
            SessionModeState.Current = SessionMode.Guided;

            EventBus.Instance.RaiseSafetyViolation(new SafetyViolationEventArgs { ViolationCode = "TEST" });
            ProcessEvents();

            Assert.IsTrue(_manager.Arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Failure, kind);
        }

        [Test]
        public void GuidedMode_CriticalFailure_RequestsCritical()
        {
            SessionModeState.Current = SessionMode.Guided;

            EventBus.Instance.RaiseCriticalSafetyFailure(new CriticalSafetyFailureEventArgs { Reason = "TEST" });
            ProcessEvents();

            Assert.IsTrue(_manager.Arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Critical, kind);
        }

        [Test]
        public void EvaluationMode_CompliantTaskCompleted_RequestsSuccess()
        {
            SessionModeState.Current = SessionMode.Evaluation;

            EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs { WasPpeCompliant = true });
            ProcessEvents();

            Assert.IsTrue(_manager.Arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Success, kind);
        }

        [Test]
        public void EvaluationMode_UnsafeTaskCompleted_StillRequestsSuccess()
        {
            SessionModeState.Current = SessionMode.Evaluation;

            EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs { WasPpeCompliant = false });
            ProcessEvents();

            Assert.IsTrue(_manager.Arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Success, kind, "Unsafe tasks in Evaluation mode must play generic completion sound, not unsafe success.");
        }

        [Test]
        public void EvaluationMode_SafetyViolation_IsSuppressed()
        {
            SessionModeState.Current = SessionMode.Evaluation;

            EventBus.Instance.RaiseSafetyViolation(new SafetyViolationEventArgs { ViolationCode = "TEST" });
            ProcessEvents();

            Assert.IsFalse(_manager.Arbiter.TryResolve(0f, out _), "Safety violations in Evaluation mode must not trigger audio feedback.");
        }

        [Test]
        public void EvaluationMode_CriticalFailure_IsSuppressed()
        {
            SessionModeState.Current = SessionMode.Evaluation;

            EventBus.Instance.RaiseCriticalSafetyFailure(new CriticalSafetyFailureEventArgs { Reason = "TEST" });
            ProcessEvents();

            Assert.IsFalse(_manager.Arbiter.TryResolve(0f, out _), "Critical failure audio must be suppressed in Evaluation mode.");
        }

        [Test]
        public void EvaluationMode_UnsafeTaskWithSimultaneousViolation_PlaysOnlySuccess()
        {
            SessionModeState.Current = SessionMode.Evaluation;

            // When a task is performed unsafely, both a safety violation and a task completion fire
            EventBus.Instance.RaiseSafetyViolation(new SafetyViolationEventArgs { ViolationCode = "NO_PPE" });
            EventBus.Instance.RaiseTaskCompleted(new TaskEventArgs { WasPpeCompliant = false });
            ProcessEvents();

            Assert.IsTrue(_manager.Arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Success, kind, "Only generic success must sound, failure must not preempt it in Evaluation mode.");
        }

        [Test]
        public void EvaluationMode_PlayFailureClip_IsSuppressed()
        {
            SessionModeState.Current = SessionMode.Evaluation;

            _manager.PlayFailureClip();

            Assert.IsFalse(_manager.Arbiter.TryResolve(0f, out _));
        }

        [Test]
        public void EvaluationMode_PlayCriticalFailureClip_IsSuppressed()
        {
            SessionModeState.Current = SessionMode.Evaluation;

            _manager.PlayCriticalFailureClip();

            Assert.IsFalse(_manager.Arbiter.TryResolve(0f, out _));
        }

        [Test]
        public void EvaluationMode_PlayUnsafeSuccessClip_RedirectsToSuccess()
        {
            SessionModeState.Current = SessionMode.Evaluation;

            _manager.PlayUnsafeSuccessClip();

            Assert.IsTrue(_manager.Arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Success, kind);
        }

        [Test]
        public void EvaluationMode_GenericClip_FallsBackToSuccessClipWhenUnassigned()
        {
            _manager.ConfigureClips(_successClip, _unsafeSuccessClip, _failureClip, _criticalClip, generic: null);
            Assert.AreSame(_successClip, _manager.GenericTaskCompleteClip);
        }

        [Test]
        public void EvaluationMode_GenericClip_UsesExplicitGenericClipWhenAssigned()
        {
            _manager.ConfigureClips(_successClip, _unsafeSuccessClip, _failureClip, _criticalClip, generic: _genericClip);
            Assert.AreSame(_genericClip, _manager.GenericTaskCompleteClip);
        }
    }
}
