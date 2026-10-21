using System;
using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Feedback;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    public sealed class SuccessAdvicePresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class NarrationEntry
        {
            public string taskId;
            public AudioClip clip;
        }

        [Header("Caption")]
        // Keep the caption inside its canvas rect: OVROverlayCanvas renders only that rect, so
        // anything placed outside it is cut off on the headset.
        [SerializeField] private CanvasGroup captionGroup;
        [SerializeField] private TMP_Text captionText;
        [SerializeField, Min(0f)] private float fadeDuration = 0.25f;
        [SerializeField, Min(0f)] private float readDuration = 4f;

        [Header("Narration")]
        [SerializeField] private NarrationVoice narrationVoice;
        [SerializeField] private List<NarrationEntry> narrationClips = new();

        public event Action<string> AdviceShown;

        private Coroutine _displayRoutine;

        private void Awake()
        {
            if (captionGroup != null)
            {
                captionGroup.alpha = 0f;
                captionGroup.interactable = false;
                captionGroup.blocksRaycasts = false;
            }
        }

        private void OnEnable() => SubscribeEvents();

        private void OnDisable()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.Unsubscribe<TaskEventArgs>(OnTaskCompleted);

            if (_displayRoutine != null)
            {
                StopCoroutine(_displayRoutine);
                _displayRoutine = null;
            }

            narrationVoice?.Stop();
            if (captionGroup != null) captionGroup.alpha = 0f;
        }

        public void SubscribeEvents()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            EventBus.Instance.Subscribe<TaskEventArgs>(OnTaskCompleted);
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            var task = args.Task;
            if (task == null) return;

            bool misclassified = args.RuntimeTask != null && args.RuntimeTask.HasMisclassifiedReport;
            if (!SuccessAdvicePolicy.ShouldShow(
                    args.Phase, args.Outcome, misclassified, SessionModeState.Current, task.successAdvice))
                return;

            var clip = task.successAdviceNarrated ? FindNarration(task.id) : null;
            if (task.successAdviceNarrated && clip == null)
                SafetyLog.Warning($"[SuccessAdvicePresenter] N\u00e3o h\u00e1 narra\u00e7\u00e3o atribu\u00edda \u00e0 dica da tarefa '{task.id}'.", this);

            if (_displayRoutine != null) StopCoroutine(_displayRoutine);
            _displayRoutine = StartCoroutine(ShowAdvice(task.successAdvice, clip));
            AdviceShown?.Invoke(task.successAdvice);
        }

        private AudioClip FindNarration(string taskId)
        {
            foreach (var entry in narrationClips)
                if (entry != null && entry.taskId == taskId && entry.clip != null)
                    return entry.clip;
            return null;
        }

        private IEnumerator ShowAdvice(string advice, AudioClip clip)
        {
            if (captionText != null) captionText.text = advice;
            narrationVoice?.Play(clip);

            if (captionGroup != null)
                yield return FadeCaption(0f, 1f);

            float visibleFor = clip != null && narrationVoice != null ? clip.length : readDuration;
            if (visibleFor > 0f) yield return new WaitForSecondsRealtime(visibleFor);

            if (captionGroup != null)
                yield return FadeCaption(captionGroup.alpha, 0f);

            _displayRoutine = null;
        }

        private IEnumerator FadeCaption(float from, float to)
        {
            if (fadeDuration <= 0f)
            {
                captionGroup.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                captionGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / fadeDuration));
                yield return null;
            }

            captionGroup.alpha = to;
        }
    }
}
