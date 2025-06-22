using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays the list of tasks and the current task details,
/// </summary>
public class TaskUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TaskManager taskManager;
    [SerializeField] private EventBus eventBus;
    [SerializeField] private Transform taskListContainer;
    [SerializeField] private GameObject taskEntryPrefab;

    [Header("Current Task Detail Panel")]
    [SerializeField] private TMP_Text currentTaskOrderText;
    [SerializeField] private TMP_Text currentTaskNameText;
    [SerializeField] private TMP_Text currentTaskDescriptionText;

    private readonly Dictionary<SafetyTask, TaskEntryUI> _taskToEntryUI = new Dictionary<SafetyTask, TaskEntryUI>();

    void Start()
    {
        if (taskManager == null || eventBus == null || taskListContainer == null || taskEntryPrefab == null)
        {
            Debug.LogError("TaskUIController is missing references.", this);
            enabled = false;
            return;
        }

        PopulateTaskList();

        eventBus.onTaskStarted.AddListener(OnTaskStarted);
        eventBus.onTaskCompleted.AddListener(OnTaskCompleted);
        eventBus.onTaskTimeout.AddListener(OnTaskTimeout);

        // in case a task was already running when we came up
        var initial = taskManager.GetCurrentTaskData();
        if (initial != null) OnTaskStarted(new TaskEventArgs(initial));
    }

    void OnDestroy()
    {
        if (eventBus != null)
        {
            eventBus.onTaskStarted.RemoveListener(OnTaskStarted);
            eventBus.onTaskCompleted.RemoveListener(OnTaskCompleted);
            eventBus.onTaskTimeout.RemoveListener(OnTaskTimeout);
        }
    }

    private void PopulateTaskList()
    {
        int order = 1;
        foreach (var group in taskManager.taskGroups)
        {
            foreach (var task in group.tasks)
            {
                var go = Instantiate(taskEntryPrefab, taskListContainer);
                var entry = go.GetComponent<TaskEntryUI>();
                entry.Setup(order, task.taskName);
                _taskToEntryUI[task] = entry;
                order++;
            }
        }
    }

    private void OnTaskStarted(TaskEventArgs args)
    {
        if (args.Task == null) return;
        if (_taskToEntryUI.TryGetValue(args.Task, out var entryUI))
            entryUI.UpdateState(TaskState.InProgress);

        UpdateCurrentTaskPanel(args.Task);
    }

    private void OnTaskCompleted(TaskEventArgs args)
    {
        if (args.Task == null) return;
        if (_taskToEntryUI.TryGetValue(args.Task, out var entryUI))
            entryUI.UpdateState(TaskState.CompletedSuccess);
    }

    private void OnTaskTimeout(TaskEventArgs args)
    {
        if (args.Task == null) return;
        if (_taskToEntryUI.TryGetValue(args.Task, out var entryUI))
            entryUI.UpdateState(TaskState.CompletedFailure);
    }

    private void UpdateCurrentTaskPanel(SafetyTask task)
    {
        // purely display; index is provided by TaskManager
        int idx = taskManager.CurrentTaskIndex;
        currentTaskOrderText.text = (idx >= 0)
            ? $"{idx + 1}."
            : "";
        currentTaskNameText.text = task.taskName;
        currentTaskDescriptionText.text = task.taskDescription;
    }
}