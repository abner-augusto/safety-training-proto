using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Gameplay.Tools.Drill
{
    public class FastenerSocket : MonoBehaviour
    {
        [Header("Drive")]
        [SerializeField]
        private Transform _axis;
        [SerializeField]
        private Transform _seat;
        [SerializeField]
        private float _maxAngleDeg = 20f;
        [SerializeField]
        [Range(0f, 1f)]
        private float _minAlignment = 0.1f;
        [SerializeField]
        [Range(0f, 1f)]
        private float _minPressure = 0.2f;
        [SerializeField]
        private float _requiredDepth = 0.02f;
        [SerializeField]
        private float _speedK = 0.001f;
        [SerializeField]
        [Range(0f, 1f)]
        private float _progress;
        [SerializeField]
        private UnityEvent _onCompleted;

        [Header("Visuals")]
        [SerializeField]
        private Transform _head;

        private bool _completedFired;
        private Vector3 _headInitialLocalPosition;
        private Quaternion _headInitialLocalRotation;

        public float Progress => _progress;
        public bool IsCompleted => _progress >= 1f;

        private void Awake()
        {
            if (_head != null)
            {
                _headInitialLocalPosition = _head.localPosition;
                _headInitialLocalRotation = _head.localRotation;
            }
        }

        private void Start()
        {
            if (_progress > 0f)
            {
                UpdateHeadVisuals();
            }
            if (_progress >= 1f)
            {
                _completedFired = true;
                SnapHeadVisuals();
            }
        }

        public bool CanDrive(Vector3 bitPos, Vector3 drillDir, out float alignment, out float pressure)
        {
            drillDir = drillDir.normalized;
            Vector3 axisForward = GetAxisForward();
            float angle = Vector3.Angle(drillDir, axisForward);
            if (_maxAngleDeg <= 0f)
            {
                alignment = 0f;
            }
            else
            {
                alignment = 1f - Mathf.Clamp01(angle / _maxAngleDeg);
            }

            float required = Mathf.Max(_requiredDepth, 0.0001f);
            Vector3 seatPos = GetSeatPosition();
            Vector3 axisForwardNormalized = axisForward.normalized;
            float axial = Vector3.Dot(bitPos - seatPos, axisForwardNormalized);
            float axialAbs = Mathf.Abs(axial);
            pressure = 1f - Mathf.Clamp01(axialAbs / required);

            return alignment >= _minAlignment && pressure >= _minPressure;
        }

        public void ApplyDrive(float rpm, Vector3 bitPos, Vector3 drillDir, float dt, out bool clutching)
        {
            if (_progress >= 1f)
            {
                clutching = true;
                return;
            }

            float alignment;
            float pressure;
            if (!CanDrive(bitPos, drillDir, out alignment, out pressure))
            {
                clutching = false;
                return;
            }

            float delta = rpm * dt * alignment * pressure * _speedK;
            if (delta <= 0f)
            {
                clutching = false;
                return;
            }

            _progress = Mathf.Clamp01(_progress + delta);
            UpdateHeadVisuals();

            if (_progress >= 1f)
            {
                clutching = true;
                if (!_completedFired)
                {
                    _completedFired = true;
                    _onCompleted?.Invoke();
                }
                SnapHeadVisuals();
            }
            else
            {
                clutching = false;
            }
        }

        private void UpdateHeadVisuals()
        {
            if (_head == null)
            {
                return;
            }

            Vector3 localAxis = transform.InverseTransformDirection(GetAxisForward()).normalized;
            _head.localPosition = _headInitialLocalPosition + localAxis * (-_requiredDepth * _progress);
            _head.localRotation = Quaternion.AngleAxis(360f * _progress, localAxis) * _headInitialLocalRotation;
        }

        private void SnapHeadVisuals()
        {
            if (_head == null)
            {
                return;
            }

            Vector3 localAxis = transform.InverseTransformDirection(GetAxisForward()).normalized;
            _head.localPosition = _headInitialLocalPosition + localAxis * (-_requiredDepth);
            _head.localRotation = _headInitialLocalRotation;
        }

        private Vector3 GetAxisForward()
        {
            return _axis != null ? _axis.forward : transform.forward;
        }

        private Vector3 GetSeatPosition()
        {
            return _seat != null ? _seat.position : transform.position;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 axisForward = GetAxisForward();
            Vector3 seatPos = GetSeatPosition();

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(seatPos, seatPos + axisForward * 0.1f);
            Gizmos.DrawSphere(seatPos, 0.005f);
        }
#endif
    }
}
