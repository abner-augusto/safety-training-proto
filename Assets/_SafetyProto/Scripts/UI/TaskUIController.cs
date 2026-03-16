using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.Task;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;
using SafetyProto.Core.Logging;

namespace SafetyProto.UI
{
    /// <summary>
    /// Displays the list of tasks and the current task details.
    /// </summary>
    public class TaskUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform taskListContainer;
        [SerializeField] private GameObject taskEntryPrefab;

        [Header("Task Definitions")]
        [Tooltip("Optional static list of task groups to render. If empty, the controller will copy them from the provided TaskManager once.")]
        [SerializeField] private List<TaskGroup> taskGroups = new List<TaskGroup>();
        [Tooltip("Optional reference used only at startup to pull task definitions and initial task state.")]
        [SerializeField] private TaskManager initialTaskProvider;

        [Header("Current Task Detail Panel")]
        [SerializeField] private TMP_Text currentTaskOrderText;
        [SerializeField] private TMP_Text currentTaskNameText;
        [SerializeField] private TMP_Text currentTaskDescriptionText;
        [Header("Hint UI")]
        [SerializeField] private TMP_Text currentTaskHintText;

        private readonly Dictionary<SafetyTask, TaskEntryUI> _taskToEntryUI = new();
        private readonly Dictionary<SafetyTask, int> _taskOrderLookup = new();
        private SafetyTask _currentFocusedTask;

        private void Start()
        {
            if (taskListContainer == null || taskEntryPrefab == null)
            {
                SafetyLog.Error("TaskUIController is missing references.", this);
                enabled = false;
                return;
            }

            if (!this.IsEventBusReady())
            {
                return;
            }

            SeedTaskDefinitionsFromProvider();
            PopulateTaskList();

            EventBus.Instance.onTaskStarted.AddListener(OnTaskStarted);
            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
            EventBus.Instance.onTaskTimeout.AddListener(OnTaskTimeout);
            EventBus.Instance.onGroupStarted.AddListener(OnGroupStarted);
            EventBus.Instance.onSafetyViolation.AddListener(OnSafetyViolation);

            var initial = initialTaskProvider != null ? initialTaskProvider.GetCurrentTaskData() : null;
            if (initial != null)
            {
                OnTaskStarted(new TaskEventArgs(initial));
            }

            initialTaskProvider = null;
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onTaskStarted.RemoveListener(OnTaskStarted);
                EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
                EventBus.Instance.onTaskTimeout.RemoveListener(OnTaskTimeout);
                EventBus.Instance.onGroupStarted.RemoveListener(OnGroupStarted);
                EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
            }
        }

        private void PopulateTaskList()
        {
            int order = 1;
            foreach (var group in taskGroups)
            {
                if (group == null) continue;
                foreach (var task in group.tasks)
                {
                    var go = Instantiate(taskEntryPrefab, taskListContainer);
                    var entry = go.GetComponent<TaskEntryUI>();
                    entry.Setup(order, task.taskName);
                    _taskToEntryUI[task] = entry;
                    _taskOrderLookup[task] = order;
                    order++;
                }
            }
        }

        private void SeedTaskDefinitionsFromProvider()
        {
            if (taskGroups.Count == 0 && initialTaskProvider != null)
            {
                taskGroups = new List<TaskGroup>(initialTaskProvider.taskGroups);
            }
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            if (args.Group == null)
            {
                return;
            }

            if (args.Group.executionMode == TaskExecutionMode.FreeOrder)
            {
                foreach (var task in args.Group.tasks)
                {
                    if (_taskToEntryUI.TryGetValue(task, out var entryUI))
                    {
                        entryUI.UpdateState(TaskState.InProgress);
                    }
                }
            }
        }

        private void OnTaskStarted(TaskEventArgs args)
        {
            if (args.Task == null) return;

            _currentFocusedTask = args.Task;

            if (_taskToEntryUI.TryGetValue(args.Task, out var entryUI))
                entryUI.UpdateState(TaskState.InProgress);

            UpdateCurrentTaskPanel(args.Task);

            if (currentTaskHintText != null) currentTaskHintText.text = string.Empty;
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.Task == null) return;
            if (_taskToEntryUI.TryGetValue(args.Task, out var entryUI))
            {
                var state = args.RuntimeTask?.State ?? TaskState.CompletedSuccess;
                entryUI.UpdateState(state);
            }
        }

        private void OnTaskTimeout(TaskEventArgs args)
        {
            if (args.Task == null) return;
            if (_taskToEntryUI.TryGetValue(args.Task, out var entryUI))
                entryUI.UpdateState(TaskState.CompletedFailure);

            if (currentTaskHintText != null && args.Task == _currentFocusedTask)
            {
                currentTaskHintText.text = string.IsNullOrEmpty(args.Task.hintText)
                    ? "Time ran out!"
                    : $"Time Up: {args.Task.hintText}";
                currentTaskHintText.color = Color.red;
            }
        }

        private void OnSafetyViolation(SafetyViolationEventArgs args)
        {
            if (_currentFocusedTask == null || args.TaskId != _currentFocusedTask.taskName)
            {
                return;
            }

            if (currentTaskHintText != null)
            {
                string message = string.IsNullOrEmpty(_currentFocusedTask.hintText)
                    ? args.Message
                    : _currentFocusedTask.hintText;

                currentTaskHintText.text = message;
                currentTaskHintText.color = Color.yellow;
            }
        }

        private void UpdateCurrentTaskPanel(SafetyTask task)
        {
            if (_taskOrderLookup.TryGetValue(task, out var order))
            {
                currentTaskOrderText.text = $"{order}.";
            }
            else
            {
                currentTaskOrderText.text = string.Empty;
            }

            currentTaskNameText.text = task.taskName;
            currentTaskDescriptionText.text = task.taskDescription;
        }
    }
}
