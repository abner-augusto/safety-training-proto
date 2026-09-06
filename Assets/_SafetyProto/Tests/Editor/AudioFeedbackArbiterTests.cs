// Assets/_SafetyProto/Tests/Editor/AudioFeedbackArbiterTests.cs
//
// Feedback-sound arbitration. The regression it guards: completing a task without the required
// PPE publishes both a violation and an unsafe completion, and both clips used to be mixed on
// top of each other through PlayOneShot.
using NUnit.Framework;
using SafetyProto.Domain.Feedback;

namespace SafetyProto.Tests.Editor
{
    public class AudioFeedbackArbiterTests
    {
        private AudioFeedbackArbiter _arbiter = null!;

        [SetUp]
        public void Setup() => _arbiter = new AudioFeedbackArbiter();

        [Test]
        public void NoRequest_ResolvesToNothing()
        {
            Assert.IsFalse(_arbiter.TryResolve(0f, out _));
        }

        [Test]
        public void SingleRequest_Plays()
        {
            _arbiter.Request(AudioFeedbackKind.Success);

            Assert.IsTrue(_arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Success, kind);
        }

        [Test]
        public void UnsafeCompletionAndItsViolation_PlayOnlyTheViolation()
        {
            _arbiter.Request(AudioFeedbackKind.Failure);
            _arbiter.Request(AudioFeedbackKind.UnsafeSuccess);

            Assert.IsTrue(_arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Failure, kind);

            Assert.IsFalse(_arbiter.TryResolve(0f, out _), "the losing request is dropped, not queued");
        }

        [Test]
        public void FrameCoalescing_IgnoresArrivalOrder()
        {
            _arbiter.Request(AudioFeedbackKind.UnsafeSuccess);
            _arbiter.Request(AudioFeedbackKind.Critical);
            _arbiter.Request(AudioFeedbackKind.Success);

            Assert.IsTrue(_arbiter.TryResolve(0f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Critical, kind);
        }

        [Test]
        public void WhileAClipSounds_LowerAndEqualPriorityRequestsAreDropped()
        {
            _arbiter.Request(AudioFeedbackKind.Failure);
            Assert.IsTrue(_arbiter.TryResolve(0f, out _));
            _arbiter.NotifyPlaying(AudioFeedbackKind.Failure, 0f, clipLength: 2f);

            _arbiter.Request(AudioFeedbackKind.Success);
            Assert.IsFalse(_arbiter.TryResolve(0.5f, out _), "a success must not stack onto a failure");

            _arbiter.Request(AudioFeedbackKind.Failure);
            Assert.IsFalse(_arbiter.TryResolve(1f, out _), "nor must a second failure double up");
        }

        [Test]
        public void WhileAClipSounds_HigherPriorityInterrupts()
        {
            _arbiter.Request(AudioFeedbackKind.Failure);
            Assert.IsTrue(_arbiter.TryResolve(0f, out _));
            _arbiter.NotifyPlaying(AudioFeedbackKind.Failure, 0f, clipLength: 2f);

            _arbiter.Request(AudioFeedbackKind.Critical);

            Assert.IsTrue(_arbiter.TryResolve(0.5f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Critical, kind);
        }

        [Test]
        public void OnceTheClipEnds_AnythingPlaysAgain()
        {
            _arbiter.Request(AudioFeedbackKind.Failure);
            Assert.IsTrue(_arbiter.TryResolve(0f, out _));
            _arbiter.NotifyPlaying(AudioFeedbackKind.Failure, 0f, clipLength: 2f);

            _arbiter.Request(AudioFeedbackKind.Success);

            Assert.IsTrue(_arbiter.TryResolve(2f, out var kind));
            Assert.AreEqual(AudioFeedbackKind.Success, kind);
        }

        [Test]
        public void Reset_ForgetsPendingAndSoundingState()
        {
            _arbiter.Request(AudioFeedbackKind.Critical);
            Assert.IsTrue(_arbiter.TryResolve(0f, out _));
            _arbiter.NotifyPlaying(AudioFeedbackKind.Critical, 0f, clipLength: 5f);

            _arbiter.Request(AudioFeedbackKind.Failure);
            _arbiter.Reset();

            Assert.IsFalse(_arbiter.TryResolve(0.1f, out _), "the pending request is gone");

            _arbiter.Request(AudioFeedbackKind.Success);
            Assert.IsTrue(_arbiter.TryResolve(0.2f, out var kind), "and nothing is considered sounding");
            Assert.AreEqual(AudioFeedbackKind.Success, kind);
        }
    }
}
