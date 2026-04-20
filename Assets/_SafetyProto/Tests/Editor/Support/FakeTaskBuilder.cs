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
            public string taskName { get; set; } = string.Empty;
            public string taskDescription { get; set; } = string.Empty;
            public int successPoints { get; set; } = 100;
            public int failurePenalty { get; set; } = 10;
            public int ppePenalty { get; set; } = 20;
            public List<PPEType> requiredPPE { get; set; } = new List<PPEType>();
            public string hintText { get; set; } = string.Empty;
            public string failureAdvice { get; set; } = string.Empty;
            public string ppeAdvice { get; set; } = string.Empty;

            public string ExpectedActionId { get; set; } = string.Empty;

            IReadOnlyList<PPEType> ISafetyTask.requiredPPE => requiredPPE;
            public string ResolveExpectedActionId() => ExpectedActionId;
        }

        public sealed class FakeTaskGroup : ITaskGroup
        {
            public string groupName { get; set; } = string.Empty;
            public TaskExecutionModeShared executionMode { get; set; } = TaskExecutionModeShared.Sequential;
            public float timeLimit { get; set; } = 0f;
            public List<ISafetyTask> tasks { get; set; } = new List<ISafetyTask>();
            public List<ITaskGroup> requiredGroups { get; set; } = new List<ITaskGroup>();

            IReadOnlyList<ISafetyTask> ITaskGroup.tasks => tasks;
            IReadOnlyList<ITaskGroup> ITaskGroup.requiredGroups => requiredGroups;
        }
    }
}
