using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.PPE;
using SafetyProto.Gameplay.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.Safety
{
    /// <summary>
    /// Evaluates player actions against the active task and emits task completions or safety violations.
    /// </summary>
    public class SafetyRuleEngine : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PPEManager ppeManager;

        [Header("Settings")]
        [SerializeField] private bool verboseLogging;

        private readonly Dictionary<PPEType, bool> _ppeStates = new Dictionary<PPEType, bool>();
        private TaskGroup _activeGroup;
        private SafetyTask _activeSequentialTask;
        private readonly List<SafetyTask> _activeFreeOrderTasks = new List<SafetyTask>();

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            if (ppeManager == null)
            {
                ppeManager = FindFirstObjectByType<PPEManager>();
                if (ppeManager == null)
                {
                    Debug.LogWarning("SafetyRuleEngine: PPEManager not found. Falling back to PPE event tracking only.", this);
                }
            }

            EventBus.Instance.onActionAttempt.AddListener(HandleActionAttempt);
            EventBus.Instance.onPpeStateChanged.AddListener(HandlePpeStateChanged);
            EventBus.Instance.onGroupStarted.AddListener(OnGroupStarted);
            EventBus.Instance.onGroupCompleted.AddListener(OnGroupCompleted);
            EventBus.Instance.onTaskStarted.AddListener(OnTaskStarted);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onActionAttempt.RemoveListener(HandleActionAttempt);
                EventBus.Instance.onPpeStateChanged.RemoveListener(HandlePpeStateChanged);
                EventBus.Instance.onGroupStarted.RemoveListener(OnGroupStarted);
                EventBus.Instance.onGroupCompleted.RemoveListener(OnGroupCompleted);
                EventBus.Instance.onTaskStarted.RemoveListener(OnTaskStarted);
            }
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            _activeGroup = args.Group;
            _activeSequentialTask = null;
            _activeFreeOrderTasks.Clear();

            if (_activeGroup != null && _activeGroup.executionMode == TaskExecutionMode.FreeOrder)
            {
                _activeFreeOrderTasks.AddRange(_activeGroup.tasks);
            }
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            if (_activeGroup == args.Group)
            {
                ClearActiveContext();
            }
        }

        private void OnTaskStarted(TaskEventArgs args)
        {
            if (_activeGroup == null || args.Task == null)
            {
                return;
            }

            if (_activeGroup.executionMode == TaskExecutionMode.Sequential)
            {
                _activeSequentialTask = args.Task;
            }
        }

        private void ClearActiveContext()
        {
            _activeGroup = null;
            _activeSequentialTask = null;
            _activeFreeOrderTasks.Clear();
        }

        private void HandlePpeStateChanged(PPEStateChangedEventArgs args)
        {
            _ppeStates[args.PpeType] = args.IsWearing;
        }

        private void HandleActionAttempt(ActionAttemptEventArgs args)
        {
            if (_activeGroup == null)
            {
                RaiseViolation("NO_ACTIVE_GROUP", "Action attempted with no active task group.", null, null);
                return;
            }

            SafetyTask targetTask = null;

            if (_activeGroup.executionMode == TaskExecutionMode.Sequential)
            {
                if (_activeSequentialTask == null)
                {
                    if (verboseLogging)
                    {
                        Debug.LogWarning("SafetyRuleEngine: Sequential group active but no current task set.");
                    }
                    return;
                }

                if (_activeSequentialTask.expectedAction != args.ActionType)
                {
                    RaiseViolation(
                        "WRONG_ACTION",
                        $"Expected {_activeSequentialTask.expectedAction} but received {args.ActionType}.",
                        _activeSequentialTask,
                        _activeGroup);
                    return;
                }

                targetTask = _activeSequentialTask;
            }
            else
            {
                targetTask = _activeFreeOrderTasks.FirstOrDefault(t => t.expectedAction == args.ActionType);
                if (targetTask == null)
                {
                    RaiseViolation(
                        "WRONG_ACTION",
                        $"Action {args.ActionType} does not match any pending task in '{_activeGroup.groupName}'.",
                        null,
                        _activeGroup);
                    return;
                }
            }

            ProcessTaskAttempt(targetTask, _activeGroup);
        }

        private void ProcessTaskAttempt(SafetyTask task, TaskGroup currentGroup)
        {
            if (task == null)
            {
                return;
            }

            var runtimeTask = new RuntimeSafetyTask(task);

            bool compliant = IsPpeCompliant(task.requiredPPE);
            runtimeTask.State = compliant ? TaskState.CompletedSuccess : TaskState.CompletedSuccessButUnsafe;
            runtimeTask.HasMissedPPEOnce = !compliant;
            runtimeTask.CompletionTime = Time.time;

            if (!compliant)
            {
                RaiseViolation(
                    "PPE_MISSING",
                    $"Required PPE missing for task '{task.taskName}'.",
                    task,
                    currentGroup);
            }

            if (verboseLogging)
            {
                Debug.Log($"SafetyRuleEngine: Task '{task.taskName}' completed. PPE compliant={compliant}");
            }

            if (currentGroup != null && currentGroup.executionMode == TaskExecutionMode.FreeOrder)
            {
                _activeFreeOrderTasks.Remove(task);
            }

            TaskEvents.RaiseTaskCompleted(new TaskEventArgs(task, runtimeTask));
        }

        private bool IsPpeCompliant(IReadOnlyCollection<PPEType> requiredPpe)
        {
            if (requiredPpe == null || requiredPpe.Count == 0)
            {
                return true;
            }

            if (ppeManager != null)
            {
                return ppeManager.VerifyPPECompliance(requiredPpe.ToList());
            }

            foreach (var ppe in requiredPpe)
            {
                if (!_ppeStates.TryGetValue(ppe, out var isWearing) || !isWearing)
                {
                    return false;
                }
            }

            return true;
        }

        private void RaiseViolation(string code, string message, SafetyTask task, TaskGroup group)
        {
            SafetyEvents.RaiseSafetyViolation(new SafetyViolationEventArgs
            {
                ViolationCode = code,
                Message = message,
                TaskId = task != null ? task.taskName : string.Empty,
                GroupId = group != null ? group.groupName : string.Empty
            });
        }
    }
}
