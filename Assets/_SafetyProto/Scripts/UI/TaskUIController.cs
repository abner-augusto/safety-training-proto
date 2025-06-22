using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Controls the population and state updates of the task UI.
/// Instantiates all TaskEntry prefabs and updates their visual state based on game events.
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

    // Maps each SafetyTask ScriptableObject to its instantiated UI component.
    private readonly Dictionary<SafetyTask, TaskEntryUI> _taskToEntryUI = new Dictionary<SafetyTask, TaskEntryUI>();

    private void Start()
    {
        if (taskManager == null || eventBus == null || taskListContainer == null || taskEntryPrefab == null)
        {
            Debug.LogError("TaskUIController is missing required references.", this);
            enabled = false;
            return;
        }

        PopulateTaskList();

        eventBus.onTaskStarted.AddListener(OnTaskStarted);
        eventBus.onTaskCompleted.AddListener(OnTaskCompleted);
        eventBus.onTaskTimeout.AddListener(OnTaskTimeout);
        
        // Handle case where a task is already running at startup
        var initialTask = taskManager.GetCurrentTaskData();
        if (initialTask != null)
        {
            OnTaskStarted(new TaskEventArgs(initialTask));
        }
    }
    
    private void OnDestroy()
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
        int globalOrder = 1;
        foreach (var group in taskManager.taskGroups)
        {
            foreach (var task in group.tasks)
            {
                var go = Instantiate(taskEntryPrefab, taskListContainer);
                var entryUI = go.GetComponent<TaskEntryUI>();
                if (entryUI != null)
                {
                    entryUI.Setup(globalOrder, task.taskName);
                    _taskToEntryUI[task] = entryUI;
                    globalOrder++;
                }
            }
        }
    }

    private void OnTaskStarted(TaskEventArgs args)
    {
        if (args.Task == null) return;
        
        // Update the new task's UI to "InProgress"
        if (_taskToEntryUI.TryGetValue(args.Task, out var newEntry))
        {
            newEntry.UpdateState(TaskState.InProgress);
        }
        
        // Update the detail panel
        UpdateCurrentTaskDetails(args.Task);
    }

    private void OnTaskCompleted(TaskEventArgs args)
    {
        if (args.Task == null) return;

        if (_taskToEntryUI.TryGetValue(args.Task, out var entryUI))
        {
            entryUI.UpdateState(TaskState.CompletedSuccess);
        }
    }

    private void OnTaskTimeout(TaskEventArgs args)
    {
        if (args.Task == null) return;

        if (_taskToEntryUI.TryGetValue(args.Task, out var entryUI))
        {
            entryUI.UpdateState(TaskState.CompletedFailure);
        }
    }
    
    private void UpdateCurrentTaskDetails(SafetyTask task)
    {
        // This logic can be simplified if you track the current task index elsewhere,
        // but for now, it remains functional.
        currentTaskNameText.text = task.taskName;
        currentTaskDescriptionText.text = task.taskDescription;
    }
}