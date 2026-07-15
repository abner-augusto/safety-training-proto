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
        /// <summary>
        /// Stable, language-independent identifier for the group (e.g. "ppe_selection").
        /// Used as the analysis key in session logs; unlike <see cref="groupName"/> it is not
        /// localized. Implementations that lack an authored id fall back to <see cref="groupName"/>.
        /// </summary>
        string id { get; }

        string groupName { get; }
        TaskExecutionModeShared executionMode { get; }
        float timeLimit { get; }

        /// <summary>General objective sentence shown INSTEAD of the task list in
        /// Evaluation mode (e.g. "Prepare-se com os EPIs necessários"). Optional.</summary>
        string objective { get; }
        IReadOnlyList<ISafetyTask> tasks { get; }
        IReadOnlyList<ITaskGroup> requiredGroups { get; }
    }
}
