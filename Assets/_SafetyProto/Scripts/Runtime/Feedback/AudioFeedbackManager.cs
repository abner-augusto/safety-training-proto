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

        // One action commonly raises several feedback events (an unsafe completion publishes
        // both the PPE violation and the completion itself), so requests are collected during
        // the frame and only the most important one is played — see AudioFeedbackArbiter.
        private readonly AudioFeedbackArbiter _arbiter = new AudioFeedbackArbiter();

        // The per-clip volumes above were authored as PlayOneShot scales, i.e. relative to the
        // AudioSource's own volume. Playing through the source directly would drop that factor,
        // so it is captured once and reapplied.
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

        private void OnTaskCompleted(TaskEventArgs args)
        {
            // In Evaluation mode, play generic task complete feedback regardless of safety or compliance,
            // avoiding revealing errors to the player before the scenario ends.
            if (SessionModeState.Current == SessionMode.Evaluation)
            {
                _arbiter.Request(AudioFeedbackKind.Success);
                return;
            }

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
                _arbiter.Request(AudioFeedbackKind.Success);
            }
            else if (unsafeTask)
            {
                _arbiter.Request(AudioFeedbackKind.UnsafeSuccess);
            }
        }

        private void OnSafetyViolation(SafetyViolationEventArgs _)
        {
            // Suppress error audio in Evaluation mode so mistakes are not leaked before scenario ends.
            if (SessionModeState.Current == SessionMode.Evaluation) return;
            _arbiter.Request(AudioFeedbackKind.Failure);
        }

        private void OnCriticalFailure(CriticalSafetyFailureEventArgs _)
        {
            // Suppress error audio in Evaluation mode so mistakes are not leaked before scenario ends.
            if (SessionModeState.Current == SessionMode.Evaluation) return;
            _arbiter.Request(AudioFeedbackKind.Critical);
        }

        public void PlaySuccessClip() => _arbiter.Request(AudioFeedbackKind.Success);

        public void PlayUnsafeSuccessClip()
        {
            if (SessionModeState.Current == SessionMode.Evaluation)
            {
                _arbiter.Request(AudioFeedbackKind.Success);
                return;
            }

            _arbiter.Request(AudioFeedbackKind.UnsafeSuccess);
        }

        public void PlayFailureClip()
        {
            if (SessionModeState.Current == SessionMode.Evaluation) return;
            _arbiter.Request(AudioFeedbackKind.Failure);
        }

        public void PlayCriticalFailureClip()
        {
            if (SessionModeState.Current == SessionMode.Evaluation) return;
            _arbiter.Request(AudioFeedbackKind.Critical);
        }

        private void LateUpdate()
        {
            if (!_arbiter.TryResolve(Time.unscaledTime, out var kind)) return;

            var clip = ResolveClip(kind);
            if (clip == null || audioSource == null) return;

            // Play (not PlayOneShot) so a clip that does win over what is sounding replaces it
            // instead of mixing with it — this source belongs to the feedback manager alone.
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.volume = _baseVolume * ResolveVolume(kind);
            audioSource.Play();

            _arbiter.NotifyPlaying(kind, Time.unscaledTime, clip.length);
        }

        private AudioClip ResolveClip(AudioFeedbackKind kind)
        {
            if (SessionModeState.Current == SessionMode.Evaluation)
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
            if (SessionModeState.Current == SessionMode.Evaluation)
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
