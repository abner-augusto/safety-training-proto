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
        [Tooltip("Full success sound (task completed + all required PPE worn).")]
        [SerializeField] private AudioClip successClip;
        [Tooltip("Neutral success sound (task action performed, but missing required PPE).")]
        [SerializeField] private AudioClip unsafeSuccessClip;
        [Tooltip("Safety violation sound.")]
        [SerializeField] private AudioClip failureClip;
        [Tooltip("Critical safety failure / procedure interruption alarm sound.")]
        [SerializeField] private AudioClip criticalFailureClip;

        [Header("Volumes")]
        [SerializeField, Range(0f, 1f)] private float successVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float unsafeSuccessVolume = 0.5f;
        [SerializeField, Range(0f, 1f)] private float failureVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float criticalVolume = 1.0f;

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
            // RuntimeTask is null when SafetyRuleEngineCore publishes the completion, so
            // WasPpeCompliant is the authoritative flag for whether the task was safe.
            bool compliant = args.RuntimeTask != null
                ? args.RuntimeTask.State == TaskState.CompletedSuccess
                : args.WasPpeCompliant;

            bool unsafeTask = args.RuntimeTask != null
                ? args.RuntimeTask.State == TaskState.CompletedSuccessButUnsafe
                : !args.WasPpeCompliant;

            if (compliant)
            {
                PlayClip(successClip, successVolume);
            }
            else if (unsafeTask)
            {
                PlayClip(unsafeSuccessClip != null ? unsafeSuccessClip : successClip, unsafeSuccessVolume);
            }
        }

        private void OnSafetyViolation(SafetyViolationEventArgs _) => PlayClip(failureClip, failureVolume);

        private void OnCriticalFailure(CriticalSafetyFailureEventArgs _)
        {
            if (criticalFailureClip != null)
                PlayClip(criticalFailureClip, criticalVolume);
            else
                PlayClip(failureClip, failureVolume);
        }

        public void PlaySuccessClip() => PlayClip(successClip, successVolume);
        public void PlayUnsafeSuccessClip() => PlayClip(unsafeSuccessClip != null ? unsafeSuccessClip : successClip, unsafeSuccessVolume);
        public void PlayFailureClip() => PlayClip(failureClip, failureVolume);
        public void PlayCriticalFailureClip() => PlayClip(criticalFailureClip != null ? criticalFailureClip : failureClip, criticalVolume);

        private void PlayClip(AudioClip clip, float volume)
        {
            if (clip == null || audioSource == null) return;
            audioSource.PlayOneShot(clip, volume);
        }

        public void ResetSession() => audioSource?.Stop();
    }
}
