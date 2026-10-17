using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
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

        [Header("Modo Avaliação")]
        [Tooltip("Text shown INSTEAD of the task list in Avaliação: the group's objective sentence from the scenario JSON.")]
        [SerializeField] private TMP_Text objectiveText;

        private ITaskGroup _activeGroup;
        private List<ISafetyTask> _groupTasks       = new();
        private List<ISafetyTask> _pendingTasks     = new();
        private List<ISafetyTask> _visibleTasks     = new();
        private Dictionary<ISafetyTask, TaskEntryUI>  _taskToEntry        = new();
        private Dictionary<ISafetyTask, GameObject>   _taskToGO           = new();
        private Dictionary<ISafetyTask, Coroutine>    _removalCoroutines  = new();
        private ISafetyTask _currentSequentialTask;
        private bool _prerequisitePending;
        private int _completedVisibleCount = 0;
        private readonly AnimationCurve _scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        // Instantiating a full window of rows inside one EventBus flush cost several milliseconds
        // on the headset at every group change, so rows are created once at load and reused.
        private readonly Stack<GameObject> _rowPool = new();

        private void Awake()
        {
            if (taskListContainer == null || taskEntryPrefab == null)
            {
                SafetyLog.Error("[TaskUIController] Referências obrigatórias não atribuídas.", this);
                enabled = false;
                return;
            }

            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            EventBus.Instance.Subscribe<TaskGroupEventArgs>(OnGroupStarted);
            EventBus.Instance.Subscribe<TaskEventArgs>(OnTaskStarted);
            EventBus.Instance.Subscribe<TaskEventArgs>(OnTaskCompleted);
        }

        private void Start()
        {
            for (int i = _rowPool.Count; i < maxVisibleTasks; i++)
            {
                var go = Instantiate(taskEntryPrefab, taskListContainer);
                go.SetActive(false);
                _rowPool.Push(go);
            }
        }

        private GameObject AcquireRow()
        {
            var go = _rowPool.Count > 0 ? _rowPool.Pop() : Instantiate(taskEntryPrefab, taskListContainer);
            go.transform.SetAsLastSibling();
            go.SetActive(true);
            return go;
        }

        private void ReleaseRow(GameObject go)
        {
            go.SetActive(false);
            _rowPool.Push(go);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance == null) return;
            EventBus.Instance.Unsubscribe<TaskGroupEventArgs>(OnGroupStarted);
            EventBus.Instance.Unsubscribe<TaskEventArgs>(OnTaskStarted);
            EventBus.Instance.Unsubscribe<TaskEventArgs>(OnTaskCompleted);
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            if (args.Phase != TaskGroupPhase.Started) return;
            if (args.Group is not { } group) return;

            bool evaluation = SessionModeState.Current == SessionMode.Evaluation;
            if (objectiveText != null)
            {
                objectiveText.gameObject.SetActive(evaluation);
                if (evaluation)
                    objectiveText.text = string.IsNullOrWhiteSpace(group.objective)
                        ? group.groupName
                        : group.objective;
            }

            foreach (var kvp in _removalCoroutines)
                if (kvp.Value != null) StopCoroutine(kvp.Value);
            _removalCoroutines.Clear();

            foreach (var go in _taskToGO.Values)
                if (go != null) ReleaseRow(go);

            _taskToEntry.Clear();
            _taskToGO.Clear();
            _visibleTasks.Clear();
            _pendingTasks.Clear();
            _groupTasks.Clear();

            _activeGroup  = group;
            _currentSequentialTask = null;
            _prerequisitePending = !string.IsNullOrWhiteSpace(group.prerequisiteTaskId);
            _groupTasks   = new List<ISafetyTask>(group.tasks);
            _pendingTasks = new List<ISafetyTask>(_groupTasks);
            _completedVisibleCount = 0;

            if (evaluation)
            {
                _pendingTasks.Clear();
                if (remainingTasksText != null) remainingTasksText.text = string.Empty;
                return;
            }

            FillWindow();
            UpdateRemainingText();
        }

        private void FillWindow()
        {
            while (_visibleTasks.Count < maxVisibleTasks && _pendingTasks.Count > 0)
            {
                var task = _pendingTasks[0];
                _pendingTasks.RemoveAt(0);
                _visibleTasks.Add(task);

                int globalOrder = _groupTasks.IndexOf(task) + 1;
                var go    = AcquireRow();
                var entry = go.GetComponent<TaskEntryUI>();

                if (entry == null)
                {
                    SafetyLog.Warning("[TaskUIController] taskEntryPrefab não tem TaskEntryUI.", this);
                    Destroy(go);
                    continue;
                }

                entry.Setup(globalOrder, task.taskName);
                entry.SetHighlight(HighlightFor(task));
                _taskToEntry[task] = entry;
                _taskToGO[task]    = go;

                if (gameObject.activeInHierarchy)
                    StartCoroutine(AnimateScale(go.transform, Vector3.zero, Vector3.one));
                else
                    go.transform.localScale = Vector3.one;
            }
        }

        private void OnTaskStarted(TaskEventArgs args)
        {
            if (args.Phase != TaskPhase.Started) return;
            if (args.Task is not { } task) return;
            if (_activeGroup == null || _activeGroup.executionMode != TaskExecutionModeShared.Sequential) return;

            _currentSequentialTask = task;
            RefreshHighlights();
        }

        private TaskHighlight HighlightFor(ISafetyTask task)
        {
            if (_activeGroup == null) return TaskHighlight.None;

            if (_prerequisitePending)
                return IsPrerequisite(task) ? TaskHighlight.Primary : TaskHighlight.None;

            if (_activeGroup.executionMode == TaskExecutionModeShared.Sequential)
                return task == _currentSequentialTask ? TaskHighlight.Primary : TaskHighlight.None;

            return TaskHighlight.Secondary;
        }

        private bool IsPrerequisite(ISafetyTask task) =>
            string.Equals(task.id, _activeGroup.prerequisiteTaskId, System.StringComparison.OrdinalIgnoreCase);

        private void RefreshHighlights()
        {
            foreach (var task in _visibleTasks)
            {
                if (_removalCoroutines.ContainsKey(task)) continue;
                if (_taskToEntry.TryGetValue(task, out var entry))
                    entry.SetHighlight(HighlightFor(task));
            }
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.Phase != TaskPhase.Completed) return;
            if (args.Task is not { } task) return;

            if (_prerequisitePending && _activeGroup != null && IsPrerequisite(task))
            {
                _prerequisitePending = false;
                RefreshHighlights();
            }

            if (!_visibleTasks.Contains(task)) return;

            var state = args.RuntimeTask?.State ?? TaskState.CompletedSuccess;
            ScheduleRemoval(task, state);
        }

        private void ScheduleRemoval(ISafetyTask task, TaskState state)
        {
            if (_removalCoroutines.ContainsKey(task)) return;

            if (_taskToEntry.TryGetValue(task, out var entry))
                entry.UpdateState(state);

            _completedVisibleCount++;
            if (!gameObject.activeInHierarchy) return;
            _removalCoroutines[task] = StartCoroutine(RemoveAfterLinger(task));
            UpdateRemainingText();
        }

        private IEnumerator RemoveAfterLinger(ISafetyTask task)
        {
            yield return new WaitForSeconds(completedLingerDuration);

            _completedVisibleCount = Mathf.Max(0, _completedVisibleCount - 1);

            if (_taskToGO.TryGetValue(task, out var go) && go != null)
                yield return AnimateScale(go.transform, Vector3.one, Vector3.zero);

            if (_taskToGO.TryGetValue(task, out var goToRelease) && goToRelease != null)
                ReleaseRow(goToRelease);

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
