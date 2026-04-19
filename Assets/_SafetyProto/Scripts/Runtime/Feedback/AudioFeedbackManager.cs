using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioFeedbackManager : MonoBehaviour, ISessionResettable
    {
        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip successClip;
        [SerializeField] private AudioClip failureClip;
        [SerializeField, Range(0f, 1f)] private float successVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float failureVolume = 0.8f;

        private void Awake() => audioSource ??= GetComponent<AudioSource>();

        private void OnEnable()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
            EventBus.Instance.onSafetyViolation.AddListener(OnSafetyViolation);
            EventBus.Instance.onCriticalSafetyFailure.AddListener(OnCriticalFailure);
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
                EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
                EventBus.Instance.onCriticalSafetyFailure.RemoveListener(OnCriticalFailure);
            }
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.RuntimeTask?.State == TaskState.CompletedFailure) return;
            PlayClip(successClip, successVolume);
        }

        private void OnSafetyViolation(SafetyViolationEventArgs _) => PlayClip(failureClip, failureVolume);
        private void OnCriticalFailure(CriticalSafetyFailureEventArgs _) => PlayClip(failureClip, failureVolume);

        public void PlaySuccessClip() => PlayClip(successClip, successVolume);
        public void PlayFailureClip() => PlayClip(failureClip, failureVolume);

        private void PlayClip(AudioClip clip, float volume)
        {
            if (clip == null || audioSource == null) return;
            audioSource.PlayOneShot(clip, volume);
        }

        public void ResetSession() => audioSource?.Stop();
    }
}
