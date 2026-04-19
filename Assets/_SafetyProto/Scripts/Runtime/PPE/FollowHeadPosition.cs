using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    public class FollowHeadPosition : MonoBehaviour
    {
        [Tooltip("Offset in world space from the head position.")]
        [SerializeField] private Vector3 offset = new Vector3(0f, -0.4f, 0.15f);

        private Transform _head;

        private void Start()
        {
            _head = transform.parent;
            if (_head == null)
            {
                SafetyLog.Error($"[FollowHeadPosition] '{name}' has no parent transform. Disabling.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            transform.position = _head.position + offset;
            float yaw = _head.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }
}