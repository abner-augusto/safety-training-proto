#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using SafetyProto.Core.Interfaces;

namespace SafetyProto.Domain.Scenarios
{
    /// <summary>
    /// JSON-backed, engine-independent <see cref="ITaskGroup"/>. Replaces the Unity
    /// <c>TaskGroup</c> ScriptableObject (as the runtime source) and the CLI's old
    /// <c>InMemoryTaskGroup</c>.
    /// </summary>
    public sealed class TaskGroupDef : ITaskGroup
    {
        [JsonProperty("name")]
        public string groupName { get; set; } = "unnamed";

        /// <summary>Raw execution-mode name as authored in JSON. Bound to the enum by the loader.</summary>
        [JsonProperty("executionMode")]
        public string ExecutionModeName { get; set; } = "Sequential";

        [JsonProperty("timeLimit")]
        public float timeLimit { get; set; }

        [JsonProperty("tasks")]
        public List<SafetyTaskDef> TaskDefs { get; set; } = new();

        /// <summary>Names of groups that must complete before this one. Resolved to refs by the loader.</summary>
        [JsonProperty("requiredGroups")]
        public List<string> RequiredGroupNames { get; set; } = new();

        [JsonIgnore]
        public TaskExecutionModeShared executionMode { get; private set; } = TaskExecutionModeShared.Sequential;

        [JsonIgnore]
        private readonly List<ITaskGroup> _requiredGroups = new();

        // IReadOnlyList<T> is covariant, so List<SafetyTaskDef> satisfies IReadOnlyList<ISafetyTask>.
        [JsonIgnore]
        IReadOnlyList<ISafetyTask> ITaskGroup.tasks => TaskDefs;

        [JsonIgnore]
        IReadOnlyList<ITaskGroup> ITaskGroup.requiredGroups => _requiredGroups;

        /// <summary>Parses the execution mode and binds each task. Loader-only.</summary>
        internal void Bind(List<string> errors)
        {
            if (System.Enum.TryParse<TaskExecutionModeShared>(ExecutionModeName, ignoreCase: true, out var mode))
            {
                executionMode = mode;
            }
            else
            {
                var valid = string.Join(", ", System.Enum.GetNames(typeof(TaskExecutionModeShared)));
                errors.Add(
                    $"executionMode desconhecido '{ExecutionModeName}' no grupo '{groupName}'. " +
                    $"Valores válidos: {valid}");
            }

            foreach (var task in TaskDefs)
            {
                task.Bind(groupName, errors);
            }
        }

        internal void AddRequiredGroup(ITaskGroup group) => _requiredGroups.Add(group);
    }
}
