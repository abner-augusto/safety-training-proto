using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    public class TaskPopupFeedback : MonoBehaviour, ISessionResettable
    {
        [Header("UI References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform popupTransform;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text bodyText;

        [Header("Animation")]
        [SerializeField] private float popupDuration = 1.0f;
        [SerializeField] private float popupRiseDistance = 25f;
        [SerializeField] private float popupFadeOutTime = 0.3f;
        [SerializeField] private Color successColor = Color.cyan;
        [SerializeField] private Color warningColor = Color.yellow;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip successSound;
        [SerializeField] private AudioClip warningSound;

        [Header("Task Lookup")]
        [SerializeField] private List<SafetyTask> knownTasks = new List<SafetyTask>();

        private readonly Dictionary<string, SafetyTask> _taskLookup = new Dictionary<string, SafetyTask>();
        private Coroutine _popupRoutine;
        private Vector3 _initialLocalPosition;
        private int _lastScoreDelta;

        private void Awake()
        {
            _initialLocalPosition = popupTransform?.localPosition ?? Vector3.zero;
            BuildLookup();
            HideImmediate();
        }

        private void OnEnable()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
            EventBus.Instance.onSafetyViolation.AddListener(OnSafetyViolation);
            EventBus.Instance.onScoreChanged.AddListener(OnScoreChanged);
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
                EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
                EventBus.Instance.onScoreChanged.RemoveListener(OnScoreChanged);
            }
        }

        private void BuildLookup()
        {
            _taskLookup.Clear();
            foreach (var task in knownTasks)
            {
                if (task == null || string.IsNullOrEmpty(task.taskName)) continue;
                if (!_taskLookup.ContainsKey(task.taskName))
                    _taskLookup.Add(task.taskName, task);
            }
        }

        private void OnScoreChanged(ScoreChangedEventArgs args) => _lastScoreDelta = args.Delta;

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.RuntimeTask?.State == TaskState.CompletedFailure) return;

            string taskName = args.Task?.taskName ?? "Tarefa concluída";
            int points = _lastScoreDelta != 0 ? _lastScoreDelta : args.Task?.successPoints ?? 0;
            _lastScoreDelta = 0;

            string body = points != 0 ? $"+{points} pontos" : "Objetivo concluído";
            PlaySound(successSound);
            ShowPopup(taskName, body, successColor);
        }

        private void OnSafetyViolation(SafetyViolationEventArgs args)
        {
            string hintText = GetHint(args.TaskId);
            string message = string.IsNullOrEmpty(hintText) ? args.Message : hintText;
            PlaySound(warningSound);
            ShowPopup("Atenção", message, warningColor);
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }

        private string GetHint(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return string.Empty;
            return _taskLookup.TryGetValue(taskId, out var task) ? task.hintText : string.Empty;
        }

        private void ShowPopup(string title, string body, Color bodyColor)
        {
            if (titleText != null) titleText.text = title;
            if (bodyText != null)
            {
                bodyText.text = body;
                bodyText.color = bodyColor;
            }

            if (_popupRoutine != null) StopCoroutine(_popupRoutine);
            _popupRoutine = StartCoroutine(AnimatePopup());
        }

        private IEnumerator AnimatePopup()
        {
            if (canvasGroup == null || popupTransform == null) yield break;

            popupTransform.localPosition = _initialLocalPosition;
            canvasGroup.alpha = 0f;
            canvasGroup.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < popupDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / popupDuration);
                float timeRemaining = popupDuration - elapsed;
                float fadeAlpha = 1f;

                if (elapsed < popupFadeOutTime)
                {
                    float fadeInT = popupFadeOutTime <= 0f ? 1f : Mathf.Clamp01(elapsed / popupFadeOutTime);
                    fadeAlpha = Mathf.Lerp(0f, 1f, fadeInT);
                }
                else if (timeRemaining <= popupFadeOutTime && popupFadeOutTime > 0f)
                {
                    float fadeOutT = Mathf.Clamp01(timeRemaining / popupFadeOutTime);
                    fadeAlpha = fadeOutT;
                }

                canvasGroup.alpha = fadeAlpha;
                popupTransform.localPosition = _initialLocalPosition + Vector3.up * popupRiseDistance * t;
                yield return null;
            }

            HideImmediate();
        }

        private void HideImmediate()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.gameObject.SetActive(false);
            }

            if (popupTransform != null)
                popupTransform.localPosition = _initialLocalPosition;
        }

        public void ResetSession()
        {
            _lastScoreDelta = 0;
            if (_popupRoutine != null)
            {
                StopCoroutine(_popupRoutine);
                _popupRoutine = null;
            }

            HideImmediate();
        }
    }
}
