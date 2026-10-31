using Oculus.Interaction;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Feedback;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>
    /// Brings a loose interactive object back to its home pose when a hand lets go of it away
    /// from any slot, or when it ends up out of place without being held (pushed by the physical
    /// hands, knocked off the table). The state is read from the <see cref="Grabbable"/> and the
    /// object's <see cref="ISecuredPlacement"/> components every frame, never from grab events, so
    /// nothing has to switch this component on and off around a grab.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class ReturnObjectHome : MonoBehaviour, ISessionResettable
    {
        [Tooltip("Optional Transform to use as home position/rotation. Leave empty to use the object's initial pose.")]
        [SerializeField] private Transform homeOverride;

        [Header("Out of place")]
        [Tooltip("Distance from home beyond which a loose object counts as out of place. 0 = disabled.")]
        [SerializeField] private float maxHomeDistance = 0f;

        [Tooltip("Drop below the home height beyond which a loose object counts as out of place " +
                 "(fell off the table or the platform). 0 = disabled.")]
        [SerializeField] private float maxDropBelowHome = 0.3f;

        [Tooltip("Optional area the object is meant to stay in. Leaving it counts as out of place.")]
        [SerializeField] private Collider homeZone;

        [Tooltip("Seconds a released or out-of-place object waits before the return animation begins.")]
        [SerializeField] private float returnDelay = 0f;

        [Header("Animation")]
        [SerializeField] private float returnDuration = 0.4f;
        [SerializeField] private AnimationCurve returnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private Grabbable _grabbable;
        private Rigidbody _rb;
        private ISecuredPlacement[] _placements;
        private ReturnHomePolicy _policy;

        private Vector3 _homePosition;
        private Quaternion _homeRotation;

        private bool _returning;
        private bool _physicsOwed;
        private bool _wasHeld;
        private float _heldSince;
        private float _returnProgress;
        private Vector3 _returnFromPosition;
        private Quaternion _returnFromRotation;

        /// <summary>Whether the object is currently following its animated return path.</summary>
        public bool IsReturning => _returning;

        /// <summary>Where the object returns to.</summary>
        public Pose HomePose => new Pose(_homePosition, _homeRotation);

        /// <summary>Whether a hand is holding the object right now.</summary>
        public bool IsHeld => _grabbable != null && _grabbable.SelectingPointsCount > 0;

        /// <summary>Whether a component on the object holds it in a placed pose (worn, installed).</summary>
        public bool IsSecured
        {
            get
            {
                if (_placements == null) return false;
                foreach (var placement in _placements)
                    if (placement != null && placement.IsSecured) return true;
                return false;
            }
        }

        private void Awake()
        {
            _grabbable = GetComponent<Grabbable>();
            _rb = GetComponent<Rigidbody>();
            _placements = GetComponents<ISecuredPlacement>();
            _policy = new ReturnHomePolicy(returnDelay, maxHomeDistance, maxDropBelowHome);
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

        /// <summary>Stops the animation and forgets any pending return.</summary>
        public void CancelReturn()
        {
            _policy?.Reset();
            if (!_returning) return;
            _returning = false;
            RestorePhysics();
        }

        /// <summary>Starts the return animation at once. Ignored while a hand holds the object.</summary>
        public void BeginReturnNow()
        {
            if (IsHeld) return;
            _policy?.Reset();
            BeginReturn();
        }

        /// <summary>Asks for a return after the usual delay, for an object its owner just let loose.</summary>
        public void RequestReturn() => _policy?.Request();

        public void ResetSession()
        {
            CancelReturn();
            if (IsHeld || IsSecured) return;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        public void SetHomeToCurrentPose() => CacheHomePose();

        private void BeginReturn()
        {
            _returnFromPosition = transform.position;
            _returnFromRotation = transform.rotation;
            _returnProgress = 0f;
            _returning = true;

            if (_rb != null)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
            }
        }

        private void Update()
        {
            bool held = IsHeld;
            ReportHandling(held);

            if (_returning)
            {
                if (!held)
                {
                    AdvanceReturn();
                    return;
                }

                // A hand caught the object on its way home. The SDK has just locked the body
                // kinematic and will restore the state it found, which is our animation's
                // kinematic flag, so physics must be handed back once the hand lets go.
                _returning = false;
                _physicsOwed = true;
            }

            if (!held && _physicsOwed)
            {
                // A slot or socket that took the object owns its physics now.
                if (IsSecured) _physicsOwed = false;
                else RestorePhysics();
            }

            if (_policy.Tick(Time.deltaTime, Observe(held), out _))
                BeginReturn();
        }

        private void ReportHandling(bool held)
        {
            if (held == _wasHeld) return;
            _wasHeld = held;
            if (held)
            {
                _heldSince = Time.time;
                InteractionEvents.RaiseObjectGrabbed(name);
            }
            else
            {
                InteractionEvents.RaiseObjectReleased(name, Time.time - _heldSince, IsSecured);
            }
        }

        private ReturnHomeObservation Observe(bool held)
        {
            var position = transform.position;
            bool outsideZone = homeZone != null && homeZone.enabled
                && (homeZone.ClosestPoint(position) - position).sqrMagnitude > 1e-6f;
            return new ReturnHomeObservation(
                held,
                IsSecured,
                Vector3.Distance(position, _homePosition),
                _homePosition.y - position.y,
                outsideZone);
        }

        private void AdvanceReturn()
        {
            _returnProgress = Mathf.Clamp01(_returnProgress + Time.deltaTime / Mathf.Max(returnDuration, 1e-4f));

            float t = returnCurve.Evaluate(_returnProgress);
            transform.position = Vector3.Lerp(_returnFromPosition, _homePosition, t);
            transform.rotation = Quaternion.Slerp(_returnFromRotation, _homeRotation, t);

            if (_returnProgress < 1f) return;

            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            _returning = false;
            RestorePhysics();
        }

        private void RestorePhysics()
        {
            // Writing isKinematic under the SDK's lock would hand a held object to gravity.
            if (_rb == null || _rb.IsLocked())
            {
                _physicsOwed = _rb != null;
                return;
            }
            _physicsOwed = false;
            _rb.isKinematic = false;
        }
    }
}
