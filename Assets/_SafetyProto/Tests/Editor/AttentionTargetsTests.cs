using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Domain.Feedback;

namespace SafetyProto.Tests.Editor
{
    /// <summary>The ids, labels and limits the evaluator's "Chamar atenção" picker works with.</summary>
    public class AttentionTargetsTests
    {
        [TestCase(0f, AttentionTargets.DefaultSeconds)]
        [TestCase(-3f, AttentionTargets.DefaultSeconds)]
        [TestCase(float.NaN, AttentionTargets.DefaultSeconds)]
        [TestCase(1f, AttentionTargets.MinSeconds)]
        [TestCase(12f, 12f)]
        [TestCase(600f, AttentionTargets.MaxSeconds)]
        public void Duration_IsKeptInRange(float requested, float expected)
        {
            Assert.AreEqual(expected, AttentionTargets.ClampSeconds(requested));
        }

        [Test]
        public void PpeTarget_IsNamedInPortugueseAndKeyedByType()
        {
            var info = AttentionTargets.ForPpe("Safety_Helmet", PPEType.Helmet);

            Assert.AreEqual("ppe:Safety_Helmet", info.Id);
            Assert.AreEqual("Capacete", info.Label);
            Assert.AreEqual("Helmet", info.Key);
        }

        [Test]
        public void DistractorTarget_UsesTheScenarioName_OrTheObjectName()
        {
            Assert.AreEqual("Distrator: Chinelo",
                AttentionTargets.ForDistractor("Distractor_FlipFlops", "Chinelo", "flip_flops").Label);
            Assert.AreEqual("Distrator: Distractor_FlipFlops",
                AttentionTargets.ForDistractor("Distractor_FlipFlops", null, "flip_flops").Label);
        }

        [Test]
        public void BodySlot_ListsEveryTypeItTakes()
        {
            var info = AttentionTargets.ForBodySlot("PPESlot(Head)", new[] { PPEType.Helmet, PPEType.Goggles });

            Assert.AreEqual("slot:PPESlot(Head)", info.Id);
            Assert.AreEqual("Encaixe no corpo: Capacete / Óculos de proteção", info.Label);
            Assert.IsTrue(AttentionTargets.SlotTakes(info, PPEType.Helmet));
            Assert.IsTrue(AttentionTargets.SlotTakes(info, PPEType.Goggles));
            Assert.IsFalse(AttentionTargets.SlotTakes(info, PPEType.Boots));
        }

        [Test]
        public void ScaffoldPieceAndSocket_ShareTheActionButNotTheId()
        {
            var piece = AttentionTargets.ForScaffoldPiece("GuardRailPiece", "install_guardrail", "Instalar Guarda-corpo");
            var socket = AttentionTargets.ForScaffoldSocket("GuardRailPiece", "install_guardrail", "Instalar Guarda-corpo");

            Assert.AreNotEqual(piece.Id, socket.Id);
            Assert.AreEqual(piece.Key, socket.Key);
            Assert.AreEqual("Peça: Instalar Guarda-corpo", piece.Label);
            Assert.AreEqual("Local de instalação: Instalar Guarda-corpo", socket.Label);
        }

        [Test]
        public void Mapper_SendsIdLabelAndKindToken()
        {
            var dto = DashboardDtoMapper.ToHighlightTargets(new[]
            {
                AttentionTargets.ForAnchor("Olhal", "connect_harness"),
                AttentionTargets.ForLanyard("LanyardTip", "connect_harness")
            });

            Assert.AreEqual(2, dto.targets.Length);
            Assert.AreEqual("anchor:Olhal", dto.targets[0].id);
            Assert.AreEqual("Ponto de ancoragem", dto.targets[0].label);
            Assert.AreEqual("anchor", dto.targets[0].kind);
            Assert.AreEqual("lanyard", dto.targets[1].kind);
        }
    }
}
