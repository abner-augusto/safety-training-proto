using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Domain.Scenarios;
using SafetyProto.Domain.Scoring;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers the NR-01 risk model: how the two graded axes become a level, how the retired
    /// three-tier vocabulary maps forward, and that the economy a pre-matrix scenario used is
    /// preserved exactly.
    /// </summary>
    public class RiskAssessmentTests
    {
        // ── Band boundaries ──────────────────────────────────────────────────────

        [TestCase(1, RiskLevel.Trivial)]
        [TestCase(4, RiskLevel.Trivial)]
        [TestCase(5, RiskLevel.Tolerable)]
        [TestCase(9, RiskLevel.Tolerable)]
        [TestCase(10, RiskLevel.Moderate)]
        [TestCase(15, RiskLevel.Moderate)]
        [TestCase(16, RiskLevel.Substantial)]
        [TestCase(22, RiskLevel.Substantial)]
        [TestCase(23, RiskLevel.Intolerable)]
        [TestCase(25, RiskLevel.Intolerable)]
        public void LevelForIndex_MapsEachBand(int index, RiskLevel expected)
        {
            Assert.AreEqual(expected, RiskAssessment.LevelForIndex(index));
        }

        [Test]
        public void FromGrades_DerivesLevelAndIndex()
        {
            var risk = RiskAssessment.FromGrades(severity: 5, probability: 5);

            Assert.AreEqual(25, risk.Index);
            Assert.AreEqual(RiskLevel.Intolerable, risk.Level);
            Assert.IsTrue(risk.HasGrades);
        }

        [Test]
        public void FromGrades_OnlyMaximumOfBothAxesIsIntolerable()
        {
            // Guards the band table: dropping either axis one step must fall to Substantial.
            // This is what makes "Intolerável" mean nothing interposed between worker and harm.
            Assert.AreEqual(RiskLevel.Substantial, RiskAssessment.FromGrades(5, 4).Level);
            Assert.AreEqual(RiskLevel.Substantial, RiskAssessment.FromGrades(4, 5).Level);
            Assert.AreEqual(RiskLevel.Intolerable, RiskAssessment.FromGrades(5, 5).Level);
        }

        [Test]
        public void FromGrades_ClampsOutOfRangeInsteadOfThrowing()
        {
            // One bad grade must not take a whole scenario down with it.
            var risk = RiskAssessment.FromGrades(severity: 99, probability: 0);

            Assert.AreEqual(RiskAssessment.MaxGrade, risk.Severity);
            Assert.AreEqual(RiskAssessment.MinGrade, risk.Probability);
        }

        [Test]
        public void FromLevel_HasNoGradesBehindIt()
        {
            var risk = RiskAssessment.FromLevel(RiskLevel.Substantial);

            Assert.IsFalse(risk.HasGrades);
            Assert.AreEqual(0, risk.Index);
            Assert.AreEqual(RiskLevel.Substantial, risk.Level);
        }

        // ── Vocabulary ───────────────────────────────────────────────────────────

        [TestCase("minor", RiskLevel.Tolerable)]
        [TestCase("moderate", RiskLevel.Moderate)]
        [TestCase("critical", RiskLevel.Substantial)]
        [TestCase("intolerable", RiskLevel.Intolerable)]
        [TestCase("Substancial", RiskLevel.Substantial)]
        [TestCase("TOLERÁVEL", RiskLevel.Tolerable)]
        public void TryParse_AcceptsRetiredAndPortugueseTokens(string token, RiskLevel expected)
        {
            Assert.IsTrue(RiskLevels.TryParse(token, out var level));
            Assert.AreEqual(expected, level);
        }

        [Test]
        public void TryParse_RejectsUnknownToken()
        {
            Assert.IsFalse(RiskLevels.TryParse("catastrofico", out _));
        }

        [Test]
        public void EliminatoryThreshold_CoversSubstantialAndAboveOnly()
        {
            // Regression guard: the medal rule used to test equality against the single top
            // tier, so adding a new top tier silently dropped the worst task out of it.
            // Enumerate every current level explicitly (not a cherry-picked subset), including
            // one that compares the threshold to itself, so a future tier inserted anywhere in
            // the order — or a threshold that stops covering its own level — is caught here.
            Assert.IsFalse(RiskLevel.Trivial >= RiskLevels.EliminatoryThreshold);
            Assert.IsFalse(RiskLevel.Tolerable >= RiskLevels.EliminatoryThreshold);
            Assert.IsFalse(RiskLevel.Moderate >= RiskLevels.EliminatoryThreshold);
            Assert.IsTrue(RiskLevel.Substantial >= RiskLevels.EliminatoryThreshold);
            Assert.IsTrue(RiskLevel.Intolerable >= RiskLevels.EliminatoryThreshold);
        }

        // ── Scoring config ───────────────────────────────────────────────────────

        [Test]
        public void ScoringConfig_LegacyFlatKeys_PreserveTheOldEconomy()
        {
            // Values are deliberately different from ScoringConfig's built-in Defaults
            // (100/150/200 points, 30/50/100 penalty, 0.7/0.5/0.0 unsafeFactor). Using the
            // default numbers here would let a broken (or entirely deleted) legacy-key
            // migration pass by accident, since PointsFor/BasePenaltyFor/UnsafeFactorFor would
            // fall through to those very same defaults regardless of whether the legacy keys
            // were ever read.
            var load = ScenarioLoader.Parse(@"{
                ""name"": ""legacy"",
                ""scoring"": {
                    ""criticalPoints"": 220, ""moderatePoints"": 160, ""minorPoints"": 90,
                    ""criticalPenalty"": 90, ""moderatePenalty"": 45, ""minorPenalty"": 25,
                    ""criticalUnsafeFactor"": 0.1, ""moderateUnsafeFactor"": 0.4, ""minorUnsafeFactor"": 0.6
                },
                ""groups"": []
            }");

            Assert.IsTrue(load.Success, load.ErrorSummary);
            var scoring = load.Scenario!.Scoring;

            Assert.AreEqual(90, scoring.PointsFor(RiskLevel.Tolerable), "minor -> tolerable");
            Assert.AreEqual(160, scoring.PointsFor(RiskLevel.Moderate));
            Assert.AreEqual(220, scoring.PointsFor(RiskLevel.Substantial), "critical -> substantial");

            Assert.AreEqual(25, scoring.BasePenaltyFor(RiskLevel.Tolerable));
            Assert.AreEqual(45, scoring.BasePenaltyFor(RiskLevel.Moderate));
            Assert.AreEqual(90, scoring.BasePenaltyFor(RiskLevel.Substantial));

            Assert.AreEqual(0.6, scoring.UnsafeFactorFor(RiskLevel.Tolerable));
            Assert.AreEqual(0.4, scoring.UnsafeFactorFor(RiskLevel.Moderate));
            Assert.AreEqual(0.1, scoring.UnsafeFactorFor(RiskLevel.Substantial));

            // Tiers the legacy vocabulary had no word for fall back to the defaults, untouched.
            Assert.AreEqual(50, scoring.PointsFor(RiskLevel.Trivial));
            Assert.AreEqual(250, scoring.PointsFor(RiskLevel.Intolerable));
        }

        [Test]
        public void ScoringConfig_LevelsBlock_WinsOverLegacyKeys()
        {
            var load = ScenarioLoader.Parse(@"{
                ""name"": ""mixed"",
                ""scoring"": {
                    ""levels"": { ""moderate"": { ""points"": 999, ""penalty"": 5, ""unsafeFactor"": 0.1 } },
                    ""moderatePoints"": 150
                },
                ""groups"": []
            }");

            Assert.IsTrue(load.Success, load.ErrorSummary);
            Assert.AreEqual(999, load.Scenario!.Scoring.PointsFor(RiskLevel.Moderate));
        }

        // ── Task authoring ───────────────────────────────────────────────────────

        [Test]
        public void TaskDef_GradedRisk_DerivesLevel()
        {
            var load = ScenarioLoader.Parse(@"{
                ""name"": ""graded"",
                ""groups"": [{
                    ""id"": ""g"", ""name"": ""g"", ""executionMode"": ""FreeOrder"",
                    ""tasks"": [{
                        ""id"": ""connect_lanyard"", ""name"": ""Talabarte"", ""actionId"": ""connect_harness"",
                        ""risk"": { ""severity"": 5, ""probability"": 5 }
                    }]
                }]
            }");

            Assert.IsTrue(load.Success, load.ErrorSummary);
            var task = load.Scenario!.Groups[0].TaskDefs[0];

            Assert.AreEqual(RiskLevel.Intolerable, task.riskLevel);
            Assert.AreEqual(5, task.risk.Severity);
            Assert.AreEqual(5, task.risk.Probability);
            Assert.IsTrue(task.risk.HasGrades);
        }

        [Test]
        public void TaskDef_LegacySeverityToken_StillLoads()
        {
            var load = ScenarioLoader.Parse(@"{
                ""name"": ""legacy"",
                ""groups"": [{
                    ""id"": ""g"", ""name"": ""g"", ""executionMode"": ""Sequential"",
                    ""tasks"": [{ ""id"": ""t"", ""name"": ""t"", ""actionId"": ""a"", ""severity"": ""critical"" }]
                }]
            }");

            Assert.IsTrue(load.Success, load.ErrorSummary);
            var task = load.Scenario!.Groups[0].TaskDefs[0];

            Assert.AreEqual(RiskLevel.Substantial, task.riskLevel);
            Assert.IsFalse(task.risk.HasGrades, "A declared level carries no grades behind it.");
        }

        [Test]
        public void TaskDef_UnknownLevelToken_FailsToLoad()
        {
            var load = ScenarioLoader.Parse(@"{
                ""name"": ""bad"",
                ""groups"": [{
                    ""id"": ""g"", ""name"": ""g"", ""executionMode"": ""Sequential"",
                    ""tasks"": [{ ""id"": ""t"", ""name"": ""t"", ""actionId"": ""a"", ""severity"": ""gravissimo"" }]
                }]
            }");

            Assert.IsFalse(load.Success);
            StringAssert.Contains("Nível de risco desconhecido", load.ErrorSummary);
        }

        [Test]
        public void TaskDef_GradeOutOfRange_FailsToLoad()
        {
            var load = ScenarioLoader.Parse(@"{
                ""name"": ""bad"",
                ""groups"": [{
                    ""id"": ""g"", ""name"": ""g"", ""executionMode"": ""Sequential"",
                    ""tasks"": [{
                        ""id"": ""t"", ""name"": ""t"", ""actionId"": ""a"",
                        ""risk"": { ""severity"": 7, ""probability"": 3 }
                    }]
                }]
            }");

            Assert.IsFalse(load.Success);
            StringAssert.Contains("entre 1 e 5", load.ErrorSummary);
        }
    }
}
