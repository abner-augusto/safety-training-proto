using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Tests.Editor.Support
{
    public sealed class FakeTaskBuilder
    {
        public FakeSafetyTask Task(string taskName, string actionId, params PPEType[] requiredPpe)
        {
            return new FakeSafetyTask
            {
                id = taskName,
                taskName = taskName,
                ExpectedActionId = actionId,
                requiredPPE = new List<PPEType>(requiredPpe)
            };
        }

        public FakeTaskGroup Group(string groupName, TaskExecutionModeShared mode, params ISafetyTask[] tasks)
        {
            return new FakeTaskGroup
            {
                groupName = groupName,
                executionMode = mode,
                tasks = new List<ISafetyTask>(tasks)
            };
        }

        public sealed class FakeSafetyTask : ISafetyTask
        {
            public string id { get; set; } = string.Empty;
            public string taskName { get; set; } = string.Empty;
            public string taskDescription { get; set; } = string.Empty;

            private RiskAssessment _risk = RiskAssessment.Default;

            /// <summary>Settable in the fake: most fixtures only care about the tier, so they
            /// assign <see cref="riskLevel"/>; the few that exercise the graded axes assign
            /// <see cref="risk"/> directly.</summary>
            public RiskAssessment risk
            {
                get => _risk;
                set => _risk = value;
            }

            public RiskLevel riskLevel
            {
                get => _risk.Level;
                set => _risk = RiskAssessment.FromLevel(value);
            }

            public List<PPEType> requiredPPE { get; set; } = new List<PPEType>();
            public string hintText { get; set; } = string.Empty;
            public string failureAdvice { get; set; } = string.Empty;
            public string ppeAdvice { get; set; } = string.Empty;
            public string omissionAdvice { get; set; } = string.Empty;

            public string ExpectedActionId { get; set; } = string.Empty;

            public List<IReportOption> ReportOptions { get; set; } = new List<IReportOption>();
            public string reportPopupTitle { get; set; } = string.Empty;
            public string reportPopupBody { get; set; } = string.Empty;
            public string reportConfirmLabel { get; set; } = string.Empty;
            public string reportCancelLabel { get; set; } = string.Empty;

            IReadOnlyList<PPEType> ISafetyTask.requiredPPE => requiredPPE;
            IReadOnlyList<IReportOption> ISafetyTask.reportOptions => ReportOptions;
            public string ResolveExpectedActionId() => ExpectedActionId;
        }

        /// <summary>Minimal <see cref="IReportOption"/> for fixtures.</summary>
        public sealed class FakeReportOption : IReportOption
        {
            public string Id { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public bool Correct { get; set; }
        }

        public sealed class FakeTaskGroup : ITaskGroup
        {
            public string id => groupName;
            public string groupName { get; set; } = string.Empty;
            public TaskExecutionModeShared executionMode { get; set; } = TaskExecutionModeShared.Sequential;
            public float timeLimit { get; set; } = 0f;
            public string objective { get; set; } = string.Empty;
            public string prerequisiteTaskId { get; set; } = string.Empty;
            public string prerequisiteAdvice { get; set; } = string.Empty;
            public List<ISafetyTask> tasks { get; set; } = new List<ISafetyTask>();
            public List<ITaskGroup> requiredGroups { get; set; } = new List<ITaskGroup>();

            IReadOnlyList<ISafetyTask> ITaskGroup.tasks => tasks;
            IReadOnlyList<ITaskGroup> ITaskGroup.requiredGroups => requiredGroups;
        }
    }
}
