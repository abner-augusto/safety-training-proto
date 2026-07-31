using System;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Domain.Scoring;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class DashboardDtoMapperTests
    {
        private FakeTaskBuilder _builder = null!;

        [SetUp]
        public void Setup() => _builder = new FakeTaskBuilder();

        [TestCase(TaskState.InProgress, "active")]
        [TestCase(TaskState.CompletedSuccess, "completed")]
        [TestCase(TaskState.CompletedSuccessButUnsafe, "completed")]
        [TestCase(TaskState.CompletedFailure, "failed")]
        [TestCase(TaskState.NotStarted, "pending")]
        public void ResolveTaskStatus_MapsStateToWireString(TaskState state, string expected)
        {
            Assert.AreEqual(expected, DashboardDtoMapper.ResolveTaskStatus(state));
        }

        [Test]
        public void BuildTaskMetadata_ResolvesOrderAndGroupWithinKnownGroups()
        {
            var t1 = _builder.Task("t1", "a1");
            var t2 = _builder.Task("t2", "a2");
            var t3 = _builder.Task("t3", "a3");
            var group = _builder.Group("G", TaskExecutionModeShared.Sequential, t1, t2, t3);
            var groups = new ITaskGroup[] { group };

            var meta = DashboardDtoMapper.BuildTaskMetadata(t2, groups, ScoringConfig.Default);

            Assert.AreEqual("G", meta.groupName);
            Assert.AreEqual("Sequential", meta.executionMode);
            Assert.AreEqual(2, meta.order);
        }

        [Test]
        public void BuildTaskMetadata_NullTask_ReturnsEmpty()
        {
            var meta = DashboardDtoMapper.BuildTaskMetadata(
                null, Array.Empty<ITaskGroup>(), ScoringConfig.Default);

            Assert.AreEqual(-1, meta.order);
            Assert.AreEqual(string.Empty, meta.groupName);
            Assert.IsEmpty(meta.requiredPpe);
        }

        [Test]
        public void BuildTaskMetadata_IncludeDetailsFalse_OmitsPpeAndExpectedAction()
        {
            var t1 = _builder.Task("t1", "a1", PPEType.Helmet);
            var group = _builder.Group("G", TaskExecutionModeShared.Sequential, t1);
            var groups = new ITaskGroup[] { group };

            var meta = DashboardDtoMapper.BuildTaskMetadata(
                t1, groups, ScoringConfig.Default, includeDetails: false);

            Assert.IsEmpty(meta.requiredPpe);
            Assert.AreEqual(string.Empty, meta.expectedAction);
        }

        [Test]
        public void BuildTaskDto_UsesTaskNameAsIdAndAppliesScoringForRiskLevel()
        {
            var scoring = ScoringConfig.Default;
            var task = _builder.Task("weld_pipe", "action_weld", PPEType.Helmet);
            task.riskLevel = RiskLevel.Substantial;
            var group = _builder.Group("G", TaskExecutionModeShared.Sequential, task);
            var groups = new ITaskGroup[] { group };

            var args = new TaskEventArgs(task, null, TaskPhase.Completed)
            {
                SessionId = "S1",
                TimestampMs = 1234L
            };

            var dto = DashboardDtoMapper.BuildTaskDto(args, "completed", groups, scoring);

            Assert.AreEqual("weld_pipe", dto.taskId);
            Assert.AreEqual("weld_pipe", dto.taskName);
            Assert.AreEqual("completed", dto.status);
            Assert.AreEqual("S1", dto.sessionId);
            Assert.AreEqual(1234L, dto.timestampMs);
            Assert.AreEqual(scoring.PointsFor(RiskLevel.Substantial), dto.successPoints);
            Assert.AreEqual(scoring.BasePenaltyFor(RiskLevel.Substantial), dto.failurePenalty);
            Assert.AreEqual(
                scoring.PointsFor(RiskLevel.Substantial) - scoring.UnsafeEarnFor(RiskLevel.Substantial),
                dto.ppePenalty);
            CollectionAssert.Contains(dto.requiredPpe, "Helmet");
        }

        [Test]
        public void BuildManifestItem_CarriesStatusAndResolvedOrder()
        {
            var t1 = _builder.Task("t1", "a1");
            var t2 = _builder.Task("t2", "a2");
            var group = _builder.Group("G", TaskExecutionModeShared.Sequential, t1, t2);
            var groups = new ITaskGroup[] { group };

            var item = DashboardDtoMapper.BuildManifestItem(t2, groups, ScoringConfig.Default, "active");

            Assert.AreEqual("t2", item.taskName);
            Assert.AreEqual("G", item.groupName);
            Assert.AreEqual(2, item.order);
            Assert.AreEqual("active", item.status);
        }
    }
}
