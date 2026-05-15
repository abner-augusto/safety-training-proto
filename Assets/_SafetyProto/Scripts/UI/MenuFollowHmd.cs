using UnityEngine;
using SafetyProto.Core.Logging;

namespace SafetyProto.UI
{
    public class MenuFollowHmd : MonoBehaviour
    {
        [Header("HMD Source")]
        [Tooltip("The HMD camera transform to follow. Auto-assigned from Camera.main if left empty.")]
        [SerializeField]
        private Transform hmdTransform;

        [Header("Positioning")]
        [Tooltip("How far in front of the HMD the menu floats, in metres.")]
        public float followDistance = 2f;
        [Tooltip("Additional offset applied in HMD local space after the forward distance. Use Y to raise/lower the menu.")]
        public Vector3 menuOffset = Vector3.zero;
        [Tooltip("Minimum world-space distance the menu must drift before it starts moving towards the new target position. Prevents micro-jitter.")]
        public float positionDeadzone = 0.05f;

        [Header("Smoothing")]
        [Tooltip("Lerp speed for position catch-up. Higher values make the menu snap faster to the target.")]
        public float positionLerpSpeed = 5f;
        [Tooltip("Slerp speed for rotation catch-up. Higher values make the menu face the player more aggressively.")]
        public float rotationLerpSpeed = 7f;
        [Tooltip("Minimum angle delta (degrees) between current and target rotation before the menu starts rotating. Prevents constant spinning on small head movements.")]
        public float rotationDeadzoneDegrees = 15f;

        private void Start()
        {
            if (hmdTransform == null)
            {
                if (Camera.main != null)
                    hmdTransform = Camera.main.transform;
                else
                SafetyLog.Error("MenuFollowHmd: Please assign the HMD Transform (Camera) in the Inspector!", this);
            }
        }

        private void LateUpdate()
        {
            if (hmdTransform == null) return;

            Vector3 targetPosition = hmdTransform.position + hmdTransform.forward * followDistance + hmdTransform.TransformVector(menuOffset);
            float distance = Vector3.Distance(transform.position, targetPosition);
            if (distance > positionDeadzone)
            {
                transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * positionLerpSpeed);
            }

            Vector3 lookDirection = (transform.position - hmdTransform.position).normalized;
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            float angleDelta = Quaternion.Angle(transform.rotation, targetRotation);
            if (angleDelta > rotationDeadzoneDegrees)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationLerpSpeed);
            }
        }
    }
}
