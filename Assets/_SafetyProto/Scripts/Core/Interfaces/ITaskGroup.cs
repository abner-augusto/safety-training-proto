using System.Collections.Generic;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Execution mode for a task group, shared by JSON, Unity runtime, and CLI harness.
    /// </summary>
    public enum TaskExecutionModeShared
    {
        Sequential,
        FreeOrder
    }

    /// <summary>
    /// Engine-independent view of a group of safety tasks.
    /// </summary>
    public interface ITaskGroup
    {
        string groupName { get; }
        TaskExecutionModeShared executionMode { get; }
        float timeLimit { get; }
        IReadOnlyList<ISafetyTask> tasks { get; }
        IReadOnlyList<ITaskGroup> requiredGroups { get; }
    }
}
