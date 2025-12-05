using UnityEngine;
using SafetyProto.Core.Logging;

namespace SafetyProto.UI
{
    public class MenuFollowHmd : MonoBehaviour
    {
        [Header("HMD Source")]
        [SerializeField]
        private Transform hmdTransform;

        [Header("Positioning")]
        public float followDistance = 2f;
        public Vector3 menuOffset = Vector3.zero;
        public float positionDeadzone = 0.05f;

        [Header("Smoothing")]
        public float positionLerpSpeed = 5f;
        public float rotationLerpSpeed = 7f;
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
