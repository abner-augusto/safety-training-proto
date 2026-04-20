using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Runtime.Task;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    public class TaskUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform taskListContainer;
        [SerializeField] private GameObject taskEntryPrefab;

        [Header("Janela Deslizante")]
        [SerializeField] private int maxVisibleTasks = 5;
        [SerializeField] private float completedLingerDuration = 1.5f;
        [SerializeField] private float entryAnimDuration = 0.2f;

        [Header("Rodapé")]
        [SerializeField] private TMP_Text remainingTasksText;

        private TaskGroup _activeGroup;
        private List<SafetyTask> _groupTasks       = new();
        private List<SafetyTask> _pendingTasks     = new();
        private List<SafetyTask> _visibleTasks     = new();
        private Dictionary<SafetyTask, TaskEntryUI>  _taskToEntry        = new();
        private Dictionary<SafetyTask, GameObject>   _taskToGO           = new();
        private Dictionary<SafetyTask, Coroutine>    _removalCoroutines  = new();
        private int _completedVisibleCount = 0;
        private readonly AnimationCurve _scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        private void Awake()
        {
            if (taskListContainer == null || taskEntryPrefab == null)
            {
                SafetyLog.Error("[TaskUIController] Referências obrigatórias não atribuídas.", this);
                enabled = false;
                return;
            }

            if (EventBus.Instance == null)
            {
                SafetyLog.Warning("[TaskUIController] EventBus.Instance não encontrado.", this);
                return;
            }

            EventBus.Instance.onGroupStarted.AddListener(OnGroupStarted);
            EventBus.Instance.onTaskStarted.AddListener(OnTaskStarted);
            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
            EventBus.Instance.onTaskTimeout.AddListener(OnTaskTimeout);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance == null) return;
            EventBus.Instance.onGroupStarted.RemoveListener(OnGroupStarted);
            EventBus.Instance.onTaskStarted.RemoveListener(OnTaskStarted);
            EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
            EventBus.Instance.onTaskTimeout.RemoveListener(OnTaskTimeout);
        }

        // ── Group ────────────────────────────────────────────────────────────

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            if (args.Group is not TaskGroup group) return;

            // Cancelar remoções pendentes
            foreach (var kvp in _removalCoroutines)
                if (kvp.Value != null) StopCoroutine(kvp.Value);
            _removalCoroutines.Clear();

            // Destruir entradas existentes
            foreach (var go in _taskToGO.Values)
                if (go != null) Destroy(go);

            _taskToEntry.Clear();
            _taskToGO.Clear();
            _visibleTasks.Clear();
            _pendingTasks.Clear();
            _groupTasks.Clear();

            _activeGroup  = group;
            _groupTasks   = new List<SafetyTask>(group.tasks);
            _pendingTasks = new List<SafetyTask>(_groupTasks);
            _completedVisibleCount = 0;

            FillWindow();
            UpdateRemainingText();
        }

        // ── Window ───────────────────────────────────────────────────────────

        private void FillWindow()
        {
            while (_visibleTasks.Count < maxVisibleTasks && _pendingTasks.Count > 0)
            {
                var task = _pendingTasks[0];
                _pendingTasks.RemoveAt(0);
                _visibleTasks.Add(task);

                int globalOrder = _groupTasks.IndexOf(task) + 1;
                var go    = Instantiate(taskEntryPrefab, taskListContainer);
                var entry = go.GetComponent<TaskEntryUI>();

                if (entry == null)
                {
                    SafetyLog.Warning("[TaskUIController] taskEntryPrefab não tem TaskEntryUI.", this);
                    Destroy(go);
                    continue;
                }

                entry.Setup(globalOrder, task.taskName);
                _taskToEntry[task] = entry;
                _taskToGO[task]    = go;

                StartCoroutine(AnimateScale(go.transform, Vector3.zero, Vector3.one));
            }
        }

        // ── Task events ──────────────────────────────────────────────────────

        private void OnTaskStarted(TaskEventArgs args)
        {
            if (args.Task is not SafetyTask task) return;
            if (_activeGroup == null || _activeGroup.executionMode != TaskExecutionMode.Sequential) return;

            if (_taskToEntry.TryGetValue(task, out var entry))
                entry.UpdateState(TaskState.InProgress);
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.Task is not SafetyTask task) return;
            if (!_visibleTasks.Contains(task)) return;

            var state = args.RuntimeTask?.State ?? TaskState.CompletedSuccess;
            ScheduleRemoval(task, state);
        }

        private void OnTaskTimeout(TaskEventArgs args)
        {
            if (args.Task is not SafetyTask task) return;
            if (!_visibleTasks.Contains(task)) return;

            ScheduleRemoval(task, TaskState.CompletedFailure);
        }

        private void ScheduleRemoval(SafetyTask task, TaskState state)
        {
            if (_removalCoroutines.ContainsKey(task)) return; // já agendado

            if (_taskToEntry.TryGetValue(task, out var entry))
                entry.UpdateState(state);

            _completedVisibleCount++;
            _removalCoroutines[task] = StartCoroutine(RemoveAfterLinger(task));
            UpdateRemainingText();
        }

        // ── Coroutines ───────────────────────────────────────────────────────

        private IEnumerator RemoveAfterLinger(SafetyTask task)
        {
            yield return new WaitForSeconds(completedLingerDuration);

            _completedVisibleCount = Mathf.Max(0, _completedVisibleCount - 1);

            if (_taskToGO.TryGetValue(task, out var go) && go != null)
                yield return AnimateScale(go.transform, Vector3.one, Vector3.zero);

            if (_taskToGO.TryGetValue(task, out var goToDestroy) && goToDestroy != null)
                Destroy(goToDestroy);

            _visibleTasks.Remove(task);
            _taskToEntry.Remove(task);
            _taskToGO.Remove(task);
            _removalCoroutines.Remove(task);

            FillWindow();
            UpdateRemainingText();
        }

        private IEnumerator AnimateScale(Transform t, Vector3 from, Vector3 to)
        {
            float elapsed = 0f;
            t.localScale  = from;

            while (elapsed < entryAnimDuration)
            {
                elapsed      += Time.deltaTime;
                float pct     = _scaleCurve.Evaluate(Mathf.Clamp01(elapsed / entryAnimDuration));
                t.localScale  = Vector3.LerpUnclamped(from, to, pct);
                yield return null;
            }

            t.localScale = to;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void UpdateRemainingText()
        {
            if (remainingTasksText == null) return;

            int remaining = _pendingTasks.Count + (_visibleTasks.Count - _completedVisibleCount);

            remainingTasksText.text = remaining > 0
                ? $"Tarefas restantes: {remaining}"
                : string.Empty;
        }
    }
}
