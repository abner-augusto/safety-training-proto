using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core.Interfaces;
using UnityEngine;

namespace SafetyProto.Data.ScriptableObjects
{
    public enum TaskExecutionMode
    {
        Sequential, // User MUST do Task 1 -> Task 2 -> Task 3
        FreeOrder   // User can do Task 3 -> Task 1 -> Task 2 (all must be done to finish group)
    }

    [CreateAssetMenu(fileName = "NewTaskGroup", menuName = "VRSafetyTraining/TaskGroup", order = 2)]
    public class TaskGroup : ScriptableObject, ITaskGroup
    {
        [Header("Group Settings")]
        public string groupName = "New Task Group";
        [Tooltip("Sequential: Strict order. FreeOrder: Any order allowed within this group.")]
        public TaskExecutionMode executionMode = TaskExecutionMode.Sequential;

        [Header("Time Limits")]
        public float timeLimit = 120f;

        [Header("Tasks")]
        public List<SafetyTask> tasks = new List<SafetyTask>();

        [Header("Dependencies")]
        [Tooltip("This group cannot start until these groups are fully completed.")]
        public List<TaskGroup> requiredGroups = new List<TaskGroup>();

        #region ITaskGroup explicit implementation
        string ITaskGroup.groupName => groupName;
        TaskExecutionModeShared ITaskGroup.executionMode => (TaskExecutionModeShared)(int)executionMode;
        float ITaskGroup.timeLimit => timeLimit;
        System.Collections.Generic.IReadOnlyList<ISafetyTask> ITaskGroup.tasks => tasks;
        #endregion
    }
}
