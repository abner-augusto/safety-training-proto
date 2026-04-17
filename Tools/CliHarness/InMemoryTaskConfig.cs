using System.Collections.Generic;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;

namespace SafetyProto.CliHarness;

public sealed class InMemorySafetyTask : ISafetyTask
{
    public string taskName { get; set; } = string.Empty;
    public string taskDescription { get; set; } = string.Empty;
    public int successPoints { get; set; } = 100;
    public int failurePenalty { get; set; } = 10;
    public int ppePenalty { get; set; } = 20;
    public List<PPEType> requiredPPE { get; set; } = new();
    public string hintText { get; set; } = string.Empty;
    public string failureAdvice { get; set; } = string.Empty;
    public string ppeAdvice { get; set; } = string.Empty;

    public string ExpectedActionId { get; set; } = string.Empty;

    IReadOnlyList<PPEType> ISafetyTask.requiredPPE => requiredPPE;
    public string ResolveExpectedActionId() => ExpectedActionId;
}

public sealed class InMemoryTaskGroup : ITaskGroup
{
    public string groupName { get; set; } = string.Empty;
    public TaskExecutionModeShared executionMode { get; set; } = TaskExecutionModeShared.Sequential;
    public float timeLimit { get; set; } = 0f;
    public List<ISafetyTask> tasks { get; set; } = new();
    public List<ITaskGroup> requiredGroups { get; set; } = new();

    IReadOnlyList<ISafetyTask> ITaskGroup.tasks => tasks;
    IReadOnlyList<ITaskGroup> ITaskGroup.requiredGroups => requiredGroups;
}
