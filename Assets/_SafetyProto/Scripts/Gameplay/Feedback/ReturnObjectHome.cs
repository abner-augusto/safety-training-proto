using Oculus.Interaction;
using UnityEngine;

namespace SafetyProto.Gameplay.Interactables
{
    /// <summary>
    /// Returns a Grabbable object to a home pose when released.
    /// Optionally set a custom home Transform; otherwise the initial pose is used.
    /// Drop alongside Grabbable + Rigidbody. No other configuration needed.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class ReturnObjectHome : MonoBehaviour
    {
        [Tooltip("Optional Transform to use as home position/rotation. Leave empty to use the object's initial pose.")]
        [SerializeField] private Transform homeOverride;

        [Header("Proximity")]
        [Tooltip("Maximum distance from home before an automatic return is triggered. 0 = disabled.")]
        [SerializeField] private float maxHomeDistance = 0f;

        [Tooltip("Seconds after release before the return animation begins.")]
        [SerializeField] private float returnDelay = 0f;

        [SerializeField] private float returnDuration = 0.4f;
        [SerializeField] private AnimationCurve returnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private Grabbable _grabbable;
        private Rigidbody _rb;

        private Vector3 _homePosition;
        private Quaternion _homeRotation;

        private bool _isGrabbed;
        private bool _returning;
        private float _returnProgress;
        private Vector3 _returnFromPosition;
        private Quaternion _returnFromRotation;

        private Coroutine _delayCoroutine;

        private void Awake()
        {
            _grabbable = GetComponent<Grabbable>();
            _rb = GetComponent<Rigidbody>();
            CacheHomePose();
        }

        private void CacheHomePose()
        {
            if (homeOverride != null)
            {
                _homePosition = homeOverride.position;
                _homeRotation = homeOverride.rotation;
            }
            else
            {
                _homePosition = transform.position;
                _homeRotation = transform.rotation;
            }
        }

        private void OnEnable()
        {
            _grabbable.WhenPointerEventRaised += OnPointerEvent;
        }

        private void OnDisable()
        {
            _grabbable.WhenPointerEventRaised -= OnPointerEvent;
        }

        public void CancelReturn()
        {
            _returning = false;

            if (_delayCoroutine != null)
            {
                StopCoroutine(_delayCoroutine);
                _delayCoroutine = null;
            }

            // If we were returning, we likely forced kinematic. Let the grab system/physics take over again.
            if (_rb != null)
                _rb.isKinematic = false;
        }

        public void BeginReturnNow()
        {
            CancelReturn();
            BeginReturn();
        }

        public void RequestReturn()
        {
            CancelReturn();

            if (returnDelay > 0f)
                _delayCoroutine = StartCoroutine(ReturnAfterDelay());
            else
                BeginReturn();
        }

        private void OnPointerEvent(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select)
            {
                _isGrabbed = true;
                _returning = false;
                if (_delayCoroutine != null)
                {
                    StopCoroutine(_delayCoroutine);
                    _delayCoroutine = null;
                }
            }
            else if (evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
            {
                _isGrabbed = false;
                RequestReturn();
            }
        }

        private System.Collections.IEnumerator ReturnAfterDelay()
        {
            yield return new WaitForSeconds(returnDelay);
            _delayCoroutine = null;
            BeginReturn();
        }

        private void BeginReturn()
        {
            _returnFromPosition = transform.position;
            _returnFromRotation = transform.rotation;
            _returnProgress = 0f;
            _returning = true;

            // Kill physics momentum so it doesn't fight the return
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }
        }

        private void Update()
        {
            if (!_returning && _delayCoroutine == null && maxHomeDistance > 0f && !_isGrabbed)
            {
                float dist = Vector3.Distance(transform.position, _homePosition);
                if (dist > maxHomeDistance)
                    RequestReturn();
            }

            if (!_returning) return;

            _returnProgress += Time.deltaTime / returnDuration;
            _returnProgress = Mathf.Clamp01(_returnProgress);

            float t = returnCurve.Evaluate(_returnProgress);
            transform.position = Vector3.Lerp(_returnFromPosition, _homePosition, t);
            transform.rotation = Quaternion.Slerp(_returnFromRotation, _homeRotation, t);

            if (_returnProgress >= 1f)
            {
                transform.SetPositionAndRotation(_homePosition, _homeRotation);
                _returning = false;

                if (_rb != null)
                    _rb.isKinematic = false;
            }
        }

        /// <summary>Call this to re-read the home pose (from homeOverride if set, otherwise current transform).</summary>
        public void SetHomeToCurrentPose()
        {
            CacheHomePose();
        }
    }
}
