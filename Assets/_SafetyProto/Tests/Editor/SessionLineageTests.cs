#nullable enable
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Domain.Sessions;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// The evaluator, not the participant, names who trains. In the 26–28/10 collection a restart
    /// lost the code and "Pular" produced random ids, which cost hours of manual matching.
    /// </summary>
    public class SessionLineageTests
    {
        [TestCase("7", "P-07")]
        [TestCase("07", "P-07")]
        [TestCase("007", "P-07")]
        [TestCase("P07", "P-07")]
        [TestCase("p-7", "P-07")]
        [TestCase(" P 65 ", "P-65")]
        [TestCase("100", "P-100")]
        [TestCase("DEMO-3", "DEMO-03")]
        [TestCase("demo 12", "DEMO-12")]
        public void ParticipantCode_NormalizesWhatTheEvaluatorTypes(string raw, string expected)
        {
            Assert.IsTrue(ParticipantCode.TryNormalize(raw, out var id, out var error), error);
            Assert.AreEqual(expected, id);
            Assert.IsTrue(ParticipantCode.IsValid(id));
        }

        [TestCase("")]
        [TestCase("0")]
        [TestCase("P-00")]
        [TestCase("1000")]
        [TestCase("X-07")]
        [TestCase("P-7A")]
        public void ParticipantCode_RejectsAnythingElse(string raw)
        {
            Assert.IsFalse(ParticipantCode.TryNormalize(raw, out _, out var error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void InterruptedSession_RestartsSameParticipantChained()
        {
            var next = SessionLineage.Next(Interrupted("S1", "P-07", attempt: 1), requested: null);

            Assert.IsNotNull(next);
            Assert.AreEqual("P-07", next!.ParticipantId);
            Assert.AreEqual(ParticipantSources.Dashboard, next.Source);
            Assert.AreEqual(SessionMode.Evaluation, next.Mode);
            Assert.AreEqual("S1", next.PreviousSessionId);
            Assert.AreEqual(2, next.Attempt);
        }

        [Test]
        public void InterruptedSession_WithAnotherCode_LinksTheReplacedSessionAsAFirstAttempt()
        {
            var corrected = new SessionStartRequest("P-65", ParticipantSources.Dashboard, SessionMode.Guided);
            var next = SessionLineage.Next(Interrupted("S2", "P-06", attempt: 2), corrected);

            Assert.AreEqual("P-65", next!.ParticipantId);
            Assert.AreEqual(SessionMode.Guided, next.Mode);
            Assert.AreEqual("S2", next.PreviousSessionId);
            Assert.AreEqual(1, next.Attempt);
        }

        [Test]
        public void InterruptedSession_SameCodeNewMode_CountsAnotherAttempt()
        {
            var switched = new SessionStartRequest("P-07", ParticipantSources.Dashboard, SessionMode.Guided);
            var next = SessionLineage.Next(Interrupted("S5", "P-07", attempt: 1), switched);

            Assert.AreEqual(SessionMode.Guided, next!.Mode);
            Assert.AreEqual("S5", next.PreviousSessionId);
            Assert.AreEqual(2, next.Attempt);
        }

        [Test]
        public void CompletedSession_WaitsForTheNextParticipant()
        {
            var completed = Interrupted("S3", "P-07", attempt: 1);
            completed.Completed = true;

            Assert.IsNull(SessionLineage.Next(completed, requested: null));

            var nextParticipant = new SessionStartRequest("P-08", ParticipantSources.Dashboard, SessionMode.Guided);
            var next = SessionLineage.Next(completed, nextParticipant);
            Assert.AreEqual("P-08", next!.ParticipantId);
            Assert.AreEqual(string.Empty, next.PreviousSessionId);
            Assert.AreEqual(1, next.Attempt);
        }

        [Test]
        public void NeverStartedScene_StartsOnlyWhatWasRequested()
        {
            var idle = new SessionSnapshot();
            Assert.IsNull(SessionLineage.Next(idle, requested: null));

            var demo = new SessionStartRequest("DEMO-01", ParticipantSources.Dashboard, SessionMode.Evaluation);
            Assert.AreEqual("DEMO-01", SessionLineage.Next(idle, demo)!.ParticipantId);
        }

        [Test]
        public void InterruptedSimulatorRun_IsNotRestartedAsARealSession()
        {
            var simulated = Interrupted("S4", "SIM-1234ABCD", attempt: 1);
            simulated.Source = ParticipantSources.Simulator;

            Assert.IsNull(SessionLineage.Next(simulated, requested: null));
        }

        private static SessionSnapshot Interrupted(string sessionId, string participantId, int attempt) => new SessionSnapshot
        {
            Started = true,
            Completed = false,
            SessionId = sessionId,
            ParticipantId = participantId,
            Source = ParticipantSources.Dashboard,
            Mode = SessionMode.Evaluation,
            Attempt = attempt
        };
    }
}
