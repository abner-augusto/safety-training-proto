using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Controls the population and state updates of the task UI. Instantiates all TaskEntry prefabs in Start, highlights the current task,
/// and changes each entry's state icon when completed or failed.
/// </summary>
public class TaskUIController : MonoBehaviour
{
    [Header("References (assign in Inspector)")]
    [Tooltip("Drag in your TaskManager GameObject (with TaskManager component).")]
    [SerializeField] private TaskManager taskManager;
    [Tooltip("Parent transform under which TaskEntry prefabs will be instantiated.")]
    [SerializeField] private Transform taskListContainer;
    [Tooltip("The TaskEntry prefab (point to your TaskEntryUI prefab).")]
    [SerializeField] private GameObject taskEntryPrefab;

    [Header("Current Task Detail Panel")]
    [SerializeField] private TMP_Text currentTaskOrderText;
    [SerializeField] private TMP_Text currentTaskNameText;
    [SerializeField] private TMP_Text currentTaskDescriptionText;

    private Dictionary<SafetyTask, TaskEntryUI> _taskToEntryUI = new Dictionary<SafetyTask, TaskEntryUI>();
    private SafetyTask _previousTask;

    private void Start()
    {
        if (taskManager == null)
        {
            Debug.LogError("TaskUIController: TaskManager not assigned.");
            enabled = false;
            return;
        }

        if (!this.IsEventBusReady())
        {
            return;
        }

        if (taskListContainer == null || taskEntryPrefab == null)
        {
            Debug.LogError("TaskUIController: TaskListContainer or TaskEntryPrefab not assigned.");
            enabled = false;
            return;
        }

        PopulateTaskList();
        
        EventBus.Instance.onTaskStarted.AddListener(OnTaskStarted);
        EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
        EventBus.Instance.onTaskTimeout.AddListener(OnTaskTimeout);
        
        SafetyTask already = taskManager.GetCurrentTask();
        if (already != null)
        {
            OnTaskStarted(new TaskEventArgs(already));
        }
        else if (taskManager.taskGroups.Count > 0 && taskManager.taskGroups[0].tasks.Count > 0)
        {
            // fallback to the first task if no task is currently active
            var firstTask = taskManager.taskGroups[0].tasks[0];
            UpdateCurrentTaskDetails(firstTask);
            if (_taskToEntryUI.TryGetValue(firstTask, out var entryUI))
            {
                entryUI.SetHighlighted(true);
            }
        }
    }

    private void OnDestroy()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.onTaskStarted.RemoveListener(OnTaskStarted);
            EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
            EventBus.Instance.onTaskTimeout.RemoveListener(OnTaskTimeout);
        }
    }

    /// <summary>
    /// Instantiate one TaskEntry for each SafetyTask and cache it.
    /// </summary>
    private void PopulateTaskList()
    {
        int globalOrder = 1;
        foreach (var group in taskManager.taskGroups)
        {
            foreach (var task in group.tasks)
            {
                GameObject go = Instantiate(taskEntryPrefab, taskListContainer);
                TaskEntryUI entryUI = go.GetComponent<TaskEntryUI>();
                if (entryUI == null)
                {
                    Debug.LogError("TaskUIController: TaskEntryPrefab is missing TaskEntryUI component.");
                    continue;
                }

                entryUI.Setup(globalOrder, task.taskName);
                _taskToEntryUI[task] = entryUI;
                globalOrder++;
            }
        }
    }

    /// <summary>
    /// Called when a new task becomes current: un-highlight previous, highlight new, update detail panel.
    /// </summary>
    private void OnTaskStarted(TaskEventArgs args)
    {
        SafetyTask newTask = args.Task;
        if (newTask == null) return;

        // Un-highlight previous
        if (_previousTask != null && _taskToEntryUI.TryGetValue(_previousTask, out var oldEntry))
        {
            oldEntry.SetHighlighted(false);
        }

        // Highlight the new current task
        if (_taskToEntryUI.TryGetValue(newTask, out var newEntry))
        {
            newEntry.SetHighlighted(true);
        }
        _previousTask = newTask;

        // Update detail panel
        UpdateCurrentTaskDetails(newTask);
    }

    /// <summary>
    /// Called when a task completes successfully.
    /// </summary>
    private void OnTaskCompleted(TaskEventArgs args)
    {
        SafetyTask completed = args.Task;
        if (completed == null) return;

        if (_taskToEntryUI.TryGetValue(completed, out var entryUI))
        {
            entryUI.SetState(TaskState.Success);
        }
    }

    /// <summary>
    /// Called when a task times out (failure).
    /// </summary>
    private void OnTaskTimeout(TaskEventArgs args)
    {
        SafetyTask failedTask = args.Task;
        if (failedTask == null) return;

        if (_taskToEntryUI.TryGetValue(failedTask, out var entryUI))
        {
            entryUI.SetState(TaskState.Failure);
        }
    }

    /// <summary>
    /// Fill in CurrentTaskOrderText, Name, Description for the active task.
    /// </summary>
    private void UpdateCurrentTaskDetails(SafetyTask task)
    {
        int totalTasks = _taskToEntryUI.Count;
        int taskIndex = -1;
        int i = 1;
        foreach (var kv in _taskToEntryUI)
        {
            if (kv.Key == task)
            {
                taskIndex = i;
                break;
            }
            i++;
        }

        if (taskIndex > 0)
        {
            currentTaskOrderText.text = $"{taskIndex}";
        }
        else
        {
            currentTaskOrderText.text = " ";
        }

        currentTaskNameText.text = task.taskName;
        currentTaskDescriptionText.text = task.taskDescription;
    }
}