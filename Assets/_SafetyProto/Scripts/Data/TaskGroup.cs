using UnityEngine;
using System.Collections.Generic;

public enum TaskExecutionMode
{
    Sequential,
    FreeOrder
}

[CreateAssetMenu(fileName = "NewTaskGroup", menuName = "VRSafetyTraining/TaskGroup", order = 2)]
public class TaskGroup : ScriptableObject
{
    [Header("Group Settings")]
    public string groupName = "New Task Group";
    public TaskExecutionMode executionMode = TaskExecutionMode.Sequential;

    [Header("Tasks In This Group")]
    public List<SafetyTask> tasks = new List<SafetyTask>();

    [Header("Group Dependencies")]
    [Tooltip("These groups must be completed before this one starts.")]
    public List<TaskGroup> prerequisites = new List<TaskGroup>();
}