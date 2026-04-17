using System.Collections.Generic;

namespace SafetyProto.Core.Interfaces
{
    /// <summary>
    /// Execution mode for a task group. Mirrors the Unity
    /// <c>TaskExecutionMode</c> enum to avoid a shared dependency on the SO type.
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
    }
}
