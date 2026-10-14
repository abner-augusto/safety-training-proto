using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Domain.Scoring;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Pins the amounts and violations of the charges that belong to no task outcome
    /// (distractor PPE, inspection-gate press) and the uncharged order deviation, wired to a
    /// <see cref="FakeEventBus"/> and a real <see cref="ScoreService"/> as at runtime.
    /// </summary>
    public class IncidentalChargePolicyTests
    {
        private FakeEventBus _bus = null!;
        private ScoreService _score = null!;
        private List<SafetyViolationEventArgs> _violations = null!;

        [SetUp]
        public void Setup()
        {
            _bus = new FakeEventBus();
            _score = new ScoreService();
            _violations = new List<SafetyViolationEventArgs>();
            _bus.Subscribe<SafetyViolationEventArgs>(args => _violations.Add(args));
        }

        [Test]
        public void OrderDeviation_RaisesViolationWithoutCharge()
        {
            IncidentalChargePolicy.RecordOrderDeviation(_bus, "g1", "EPIs", new[] { "A", "B" });

            Assert.AreEqual(0, _score.CurrentScore);
            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual(ViolationCodes.OrderViolation, _violations[0].ViolationCode);
            Assert.AreEqual("g1", _violations[0].GroupId);
            Assert.AreEqual("EPIs", _violations[0].GroupName);
            Assert.AreEqual("EPIs equipados fora da ordem recomendada: A, B", _violations[0].Message);
        }

        [Test]
        public void OrderDeviation_NoDeviations_IsNoOp()
        {
            IncidentalChargePolicy.RecordOrderDeviation(_bus, "g1", "EPIs", new string[0]);

            Assert.AreEqual(0, _score.CurrentScore);
            CollectionAssert.IsEmpty(_violations);
        }

        [Test]
        public void WrongPpeSelected_FirstAttempt_ChargesOnce()
        {
            IncidentalChargePolicy.ChargeWrongPpeSelected(
                _bus, _score, ScoringConfig.Default, "Capacete", "Decoy_Helmet", firstAttemptForItem: true);

            Assert.AreEqual(-30, _score.CurrentScore);
            Assert.AreEqual(1, _violations.Count);
            Assert.AreEqual(ViolationCodes.WrongPpeSelected, _violations[0].ViolationCode);
            Assert.AreEqual("Selecionou equipamento inadequado: Capacete", _violations[0].Message);
            Assert.AreEqual("Decoy_Helmet", _violations[0].TaskName);

            int second = IncidentalChargePolicy.ChargeWrongPpeSelected(
                _bus, _score, ScoringConfig.Default, "Capacete", "Decoy_Helmet", firstAttemptForItem: false);

            Assert.AreEqual(0, second);
            Assert.AreEqual(-30, _score.CurrentScore, "A repeat attempt on the same item costs nothing more.");
            Assert.AreEqual(2, _violations.Count, "Every attempt is still logged.");
        }

        [Test]
        public void GatePenalty_UsesGateReductionFactor()
        {
            var task = new FakeTaskBuilder().Task("Guarda-corpo", "install_guardrail");
            task.riskLevel = RiskLevel.Substantial;

            var changes = new List<(int delta, string reason, string taskId)>();
            _score.ScoreChanged += (_, delta, reason, taskId) => changes.Add((delta, reason, taskId));

            int charge = IncidentalChargePolicy.ChargeGatePenalty(_score, ScoringConfig.Default, task);

            Assert.AreEqual(50, charge, "100 (substantial penalty) x 0.5 gate reduction = 50.");
            Assert.AreEqual(-50, _score.CurrentScore);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(ViolationCodes.GatePenalty, changes.Single().reason);
            Assert.AreEqual(task.id, changes.Single().taskId);
            CollectionAssert.IsEmpty(_violations, "The gate raises its own violation; the policy only charges.");
        }

        [Test]
        public void ZeroPenaltyConfig_DoesNotThrow()
        {
            var config = new ScoringConfig();
            config.Levels["tolerable"] = new RiskLevelScoring(100, 0, 0.7);

            Assert.DoesNotThrow(() =>
            {
                IncidentalChargePolicy.ChargeWrongPpeSelected(_bus, _score, config, "Capacete", "Decoy", true);
            });
            Assert.AreEqual(0, _score.CurrentScore);
        }
    }
}
