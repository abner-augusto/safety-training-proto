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
        [SerializeField] private TaskManager taskManager;
        [SerializeField] private PPEManager ppeManager;

        [Header("Settings")]
        [SerializeField] private bool verboseLogging;

        private readonly Dictionary<PPEType, bool> _ppeStates = new Dictionary<PPEType, bool>();

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            if (taskManager == null)
            {
                taskManager = FindFirstObjectByType<TaskManager>();
            }

            if (taskManager == null)
            {
                Debug.LogError("SafetyRuleEngine: TaskManager reference missing.", this);
                SafetyEvents.RaiseSafetyError(new SafetyErrorEventArgs
                {
                    Source = nameof(SafetyRuleEngine),
                    Message = "TaskManager reference missing",
                    Details = "SafetyRuleEngine could not locate TaskManager in the scene"
                });
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
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onActionAttempt.RemoveListener(HandleActionAttempt);
                EventBus.Instance.onPpeStateChanged.RemoveListener(HandlePpeStateChanged);
            }
        }

        private void HandlePpeStateChanged(PPEStateChangedEventArgs args)
        {
            _ppeStates[args.PpeType] = args.IsWearing;
        }

        private void HandleActionAttempt(ActionAttemptEventArgs args)
        {
            var currentGroup = taskManager.GetCurrentGroup();
            if (currentGroup == null)
            {
                RaiseViolation("NO_ACTIVE_GROUP", "Action attempted with no active task group.", null, null);
                return;
            }

            RuntimeSafetyTask runtimeTask = null;

            if (currentGroup.executionMode == TaskExecutionMode.Sequential)
            {
                runtimeTask = taskManager.CurrentRuntimeTask;
                if (runtimeTask == null)
                {
                    if (verboseLogging)
                    {
                        Debug.LogWarning("SafetyRuleEngine: Sequential group active but no current task set.");
                    }
                    return;
                }

                if (runtimeTask.expectedAction != args.ActionType)
                {
                    RegisterOrderViolationIfFutureTask(currentGroup, runtimeTask, args.ActionType);
                    RaiseViolation(
                        "WRONG_ACTION",
                        $"Expected {runtimeTask.expectedAction} but received {args.ActionType}.",
                        runtimeTask.TaskData,
                        currentGroup);
                    return;
                }
            }
            else
            {
                runtimeTask = taskManager.FindPendingTaskByAction(args.ActionType);
                if (runtimeTask == null)
                {
                    RaiseViolation(
                        "WRONG_ACTION",
                        $"Action {args.ActionType} does not match any pending task in '{currentGroup.groupName}'.",
                        null,
                        currentGroup);
                    return;
                }

                taskManager.FocusTask(runtimeTask);
            }

            ProcessTaskAttempt(runtimeTask, currentGroup);
        }

        private void ProcessTaskAttempt(RuntimeSafetyTask runtimeTask, TaskGroup currentGroup)
        {
            if (runtimeTask == null)
            {
                return;
            }

            if (runtimeTask.State == TaskState.CompletedSuccess || runtimeTask.State == TaskState.CompletedSuccessButUnsafe)
            {
                return;
            }

            bool compliant = IsPpeCompliant(runtimeTask.TaskData.requiredPPE);
            runtimeTask.State = compliant ? TaskState.CompletedSuccess : TaskState.CompletedSuccessButUnsafe;
            runtimeTask.HasMissedPPEOnce = !compliant;
            runtimeTask.CompletionTime = Time.time;

            if (!compliant)
            {
                RaiseViolation(
                    "PPE_MISSING",
                    $"Required PPE missing for task '{runtimeTask.taskName}'.",
                    runtimeTask.TaskData,
                    currentGroup);
            }

            if (verboseLogging)
            {
                Debug.Log($"SafetyRuleEngine: Task '{runtimeTask.taskName}' completed. PPE compliant={compliant}");
            }

            TaskEvents.RaiseTaskCompleted(new TaskEventArgs(runtimeTask.TaskData, runtimeTask));
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

        private void RegisterOrderViolationIfFutureTask(TaskGroup currentGroup, RuntimeSafetyTask runtimeTask, ActionType attemptedAction)
        {
            if (currentGroup == null || runtimeTask?.TaskData == null)
            {
                return;
            }

            var idx = currentGroup.tasks.IndexOf(runtimeTask.TaskData);
            if (idx < 0)
            {
                return;
            }

            bool futureTaskMatches = currentGroup.tasks
                .Skip(idx + 1)
                .Any(t => t.expectedAction == attemptedAction);

            if (futureTaskMatches)
            {
                taskManager.RegisterOrderViolation($"Out-of-order action {attemptedAction} while current task is {runtimeTask.expectedAction}");
            }
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
