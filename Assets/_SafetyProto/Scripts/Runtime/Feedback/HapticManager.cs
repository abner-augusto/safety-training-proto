using System.Collections;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Data.Enums;
using SafetyProto.Core.Events;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    public class HapticManager : MonoBehaviour, ISessionResettable
    {
        [Header("Click Haptic")]
        [SerializeField, Range(0f, 1f)] private float clickAmplitude = 0.25f;
        [SerializeField, Range(0f, 0.5f)] private float clickDuration = 0.05f;

        [Header("Error Haptic")]
        [SerializeField, Range(0f, 1f)] private float errorAmplitude = 0.75f;
        [SerializeField, Range(0f, 0.5f)] private float errorDuration = 0.15f;

        private int _lastInteractorId;

        private void OnEnable()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);
            EventBus.Instance.onSafetyViolation.AddListener(OnSafetyViolation);
            EventBus.Instance.onActionAttempt.AddListener(CacheInteractor);
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);
                EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
                EventBus.Instance.onActionAttempt.RemoveListener(CacheInteractor);
            }
        }

        private void CacheInteractor(ActionAttemptedEvent args) => _lastInteractorId = args.InteractorId;

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.RuntimeTask == null) return;

            var state = args.RuntimeTask.State;
            if (state == TaskState.CompletedSuccess || state == TaskState.CompletedSuccessButUnsafe)
            {
                TriggerHaptics(clickAmplitude, clickDuration);
            }
        }

        private void OnSafetyViolation(SafetyViolationEventArgs _)
            => TriggerHaptics(errorAmplitude, errorDuration);

        private void TriggerHaptics(float amplitude, float duration)
        {
#if USING_META_XR
            var controller = ResolveController(_lastInteractorId);
            StartCoroutine(HapticPulse(controller, amplitude, duration));
#else
            SafetyLog.Info($"[HapticManager] Haptic pulse -> amplitude:{amplitude:F2}, duration:{duration:F2}s", this);
#endif
        }

#if USING_META_XR
        private OVRInput.Controller ResolveController(int interactorId)
        {
            if (interactorId > 0) return OVRInput.Controller.RTouch;
            if (interactorId < 0) return OVRInput.Controller.LTouch;
            return OVRInput.Controller.Active;
        }

        private IEnumerator HapticPulse(OVRInput.Controller controller, float amplitude, float duration)
        {
            OVRInput.SetControllerVibration(amplitude, amplitude, controller);
            yield return new WaitForSeconds(duration);
            OVRInput.SetControllerVibration(0f, 0f, controller);
        }
#endif

        public void ResetSession()
        {
            StopAllCoroutines();
            _lastInteractorId = 0;
            StopAllHaptics();
        }

        private void StopAllHaptics()
        {
#if USING_META_XR
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
#endif
        }
    }
}
