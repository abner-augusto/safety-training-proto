using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Feedback;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioFeedbackManager : MonoBehaviour, ISessionResettable
    {
        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Full success sound (task completed + all required PPE worn) in Guided mode.")]
        [SerializeField] private AudioClip successClip;
        [Tooltip("Neutral success sound (task action performed, but missing required PPE) in Guided mode.")]
        [SerializeField] private AudioClip unsafeSuccessClip;
        [Tooltip("Generic task completion sound for Evaluation mode. Falls back to successClip (sucess1.mp3) if unassigned.")]
        [SerializeField] private AudioClip genericTaskCompleteClip;
        [Tooltip("Safety violation sound.")]
        [SerializeField] private AudioClip failureClip;
        [Tooltip("Critical safety failure / procedure interruption alarm sound.")]
        [SerializeField] private AudioClip criticalFailureClip;

        [Header("Volumes")]
        [SerializeField, Range(0f, 1f)] private float successVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float unsafeSuccessVolume = 0.5f;
        [SerializeField, Range(0f, 1f)] private float failureVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float criticalVolume = 1.0f;

        private readonly AudioFeedbackArbiter _arbiter = new AudioFeedbackArbiter();

        private float _baseVolume = 1f;

        public AudioClip SuccessClip => successClip;
        public AudioClip UnsafeSuccessClip => unsafeSuccessClip;
        public AudioClip GenericTaskCompleteClip => genericTaskCompleteClip != null ? genericTaskCompleteClip : successClip;
        public AudioClip FailureClip => failureClip;
        public AudioClip CriticalFailureClip => criticalFailureClip;
        public AudioFeedbackArbiter Arbiter => _arbiter;

        private void Awake()
        {
            audioSource ??= GetComponent<AudioSource>();
            if (audioSource != null && audioSource.volume > 0f) _baseVolume = audioSource.volume;
        }

        private void OnEnable() => SubscribeEvents();
        private void OnDisable() => UnsubscribeEvents();

        public void SubscribeEvents()
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

        public void UnsubscribeEvents()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
                EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
                EventBus.Instance.onCriticalSafetyFailure.RemoveListener(OnCriticalFailure);
            }

            _arbiter.Reset();
        }

        private static bool ShouldRevealOutcome(SessionMode mode) => mode != SessionMode.Evaluation;

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (!ShouldRevealOutcome(SessionModeState.Current))
            {
                _arbiter.Request(AudioFeedbackKind.Success);
                return;
            }

            var outcome = args.Outcome;
            if (outcome == TaskState.CompletedSuccess)
            {
                _arbiter.Request(AudioFeedbackKind.Success);
            }
            else if (outcome == TaskState.CompletedSuccessButUnsafe)
            {
                _arbiter.Request(AudioFeedbackKind.UnsafeSuccess);
            }
        }

        private void OnSafetyViolation(SafetyViolationEventArgs _)
        {
            if (!ShouldRevealOutcome(SessionModeState.Current)) return;
            _arbiter.Request(AudioFeedbackKind.Failure);
        }

        private void OnCriticalFailure(CriticalSafetyFailureEventArgs _)
        {
            if (!ShouldRevealOutcome(SessionModeState.Current)) return;
            _arbiter.Request(AudioFeedbackKind.Critical);
        }

        public void PlaySuccessClip() => _arbiter.Request(AudioFeedbackKind.Success);

        public void PlayUnsafeSuccessClip()
        {
            if (!ShouldRevealOutcome(SessionModeState.Current))
            {
                _arbiter.Request(AudioFeedbackKind.Success);
                return;
            }

            _arbiter.Request(AudioFeedbackKind.UnsafeSuccess);
        }

        public void PlayFailureClip()
        {
            if (!ShouldRevealOutcome(SessionModeState.Current)) return;
            _arbiter.Request(AudioFeedbackKind.Failure);
        }

        public void PlayCriticalFailureClip()
        {
            if (!ShouldRevealOutcome(SessionModeState.Current)) return;
            _arbiter.Request(AudioFeedbackKind.Critical);
        }

        private void LateUpdate()
        {
            if (!_arbiter.TryResolve(Time.unscaledTime, out var kind)) return;

            var clip = ResolveClip(kind);
            if (clip == null || audioSource == null) return;

            PlayExclusive(clip, _baseVolume * ResolveVolume(kind));

            _arbiter.NotifyPlaying(kind, Time.unscaledTime, clip.length);
        }

        private void PlayExclusive(AudioClip clip, float volume)
        {
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.volume = volume;
            audioSource.Play();
        }

        private AudioClip ResolveClip(AudioFeedbackKind kind)
        {
            if (!ShouldRevealOutcome(SessionModeState.Current))
            {
                return genericTaskCompleteClip != null ? genericTaskCompleteClip : successClip;
            }

            return kind switch
            {
                AudioFeedbackKind.Success => successClip,
                AudioFeedbackKind.UnsafeSuccess => unsafeSuccessClip != null ? unsafeSuccessClip : successClip,
                AudioFeedbackKind.Failure => failureClip,
                AudioFeedbackKind.Critical => criticalFailureClip != null ? criticalFailureClip : failureClip,
                _ => null,
            };
        }

        private float ResolveVolume(AudioFeedbackKind kind)
        {
            if (!ShouldRevealOutcome(SessionModeState.Current))
            {
                return successVolume;
            }

            return kind switch
            {
                AudioFeedbackKind.Success => successVolume,
                AudioFeedbackKind.UnsafeSuccess => unsafeSuccessVolume,
                AudioFeedbackKind.Failure => failureVolume,
                AudioFeedbackKind.Critical => criticalVolume,
                _ => 1f,
            };
        }

        public void ConfigureClips(AudioClip success, AudioClip unsafeSuccess, AudioClip failure, AudioClip critical, AudioClip generic = null)
        {
            successClip = success;
            unsafeSuccessClip = unsafeSuccess;
            failureClip = failure;
            criticalFailureClip = critical;
            genericTaskCompleteClip = generic;
        }

        public void ResetSession()
        {
            _arbiter.Reset();
            if (audioSource != null) audioSource.Stop();
        }
    }
}
