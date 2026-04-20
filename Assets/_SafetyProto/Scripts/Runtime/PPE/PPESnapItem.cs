using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    /// <summary>
    /// Placed on each PPE item root.
    /// Handles grab detection, snap-on-release, follow-slot-transform, and unsnap-on-pull.
    /// Replaces PPESlotController and all SDK snap components.
    /// Works alongside HandGrabInteractable for the actual hand grab.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PPEItem))]
    public class PPESnapItem : MonoBehaviour
    {
        [Header("Snap")]
        [Tooltip("Fallback radius to search for a compatible slot on release (helps if trigger exit happens right as you let go).")]
        [SerializeField] private float snapSearchRadius = 0.12f;
        [Tooltip("Set this to the PPE/Snap layer only for performance. Avoid ~0 (all layers) in production.")]
        [SerializeField] private LayerMask snapSearchMask = ~0;

        [Tooltip("Optional transform on this item that should align to the slot when snapped (lets you override the snap 'center' and rotation).")]
        [SerializeField] private Transform snapPoseOverride;

        [Tooltip("When snapped, re-samples the snapPoseOverride pose every frame (useful for tuning the anchor in Play Mode).")]
        [SerializeField] private bool trackSnapPoseOverrideEveryFrame = false;

        [Header("Unsnap")]
        [Tooltip("How far the hand must pull from the slot before unsnapping.")]
        [SerializeField] private float unsnapDistance = 0.15f;

        [Header("Follow")]
        [Tooltip("How fast the item follows the slot transform. 0 = instant.")]
        [SerializeField] private float followSpeed = 20f;

        [Header("SDK Reference")]
        [Tooltip("The HandGrabInteractable on this item. Auto-found if left empty.")]
        [SerializeField] private HandGrabInteractable handGrabInteractable;

        [Header("Optional Behaviors")]
        [Tooltip("If present, this will be disabled while snapped so it doesn't fight the snap pose. Auto-found if left empty.")]
        [SerializeField] private ReturnObjectHome returnObjectHome;

        // Public so PPESnapSlot can read it
        public PPEType PpeType => _ppeItem.ppeType;

        private PPEItem _ppeItem;
        private Rigidbody _rigidbody;
        private Grabbable _grabbable;

        private PPESnapSlot _currentSlot;
        private PPESnapSlot _hoveringSlot;

        private bool _isGrabbed;
        private bool _isSnapped;
        private bool _grabDisabled;

        // Cached pose of snapPoseOverride in this root's local space, used to keep a stable offset while following a slot.
        private bool _useSnapPoseOverride;
        private Vector3 _snapOverrideLocalPos;
        private Quaternion _snapOverrideLocalRot;

        private void Awake()
        {
            _ppeItem = GetComponent<PPEItem>();
            _rigidbody = GetComponent<Rigidbody>();
            _grabbable = GetComponent<Grabbable>();

            if (returnObjectHome == null)
                returnObjectHome = GetComponent<ReturnObjectHome>();

            if (handGrabInteractable == null)
                handGrabInteractable = GetComponentInChildren<HandGrabInteractable>();
        }

        private void Start()
        {
            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised += OnPointerEvent;
                return;
            }

            if (handGrabInteractable == null)
            {
                SafetyLog.Error($"PPESnapItem on {name}: Grabbable and HandGrabInteractable not found; can't detect release to snap.", this);
                enabled = false;
                return;
            }

            handGrabInteractable.WhenStateChanged += OnGrabStateChanged;
        }

        private void OnDestroy()
        {
            if (_grabbable != null)
                _grabbable.WhenPointerEventRaised -= OnPointerEvent;

            if (handGrabInteractable != null)
                handGrabInteractable.WhenStateChanged -= OnGrabStateChanged;
        }

        private void OnGrabStateChanged(InteractableStateChangeArgs args)
        {
            bool wasGrabbed = _isGrabbed;

            // InteractableState.Select = actively grabbed
            _isGrabbed = args.NewState == InteractableState.Select;

            if (!wasGrabbed && _isGrabbed)
                OnPickedUp();
            else if (wasGrabbed && !_isGrabbed)
                OnReleased();
        }

        private void OnPointerEvent(PointerEvent evt)
        {
            bool wasGrabbed = _isGrabbed;

            if (evt.Type == PointerEventType.Select)
                _isGrabbed = true;
            else if (evt.Type == PointerEventType.Unselect)
                _isGrabbed = false;
            else
                return;

            if (!wasGrabbed && _isGrabbed)
                OnPickedUp();
            else if (wasGrabbed && !_isGrabbed)
                OnReleased();
        }

        public void SetGrabEnabled(bool enabled)
        {
            _grabDisabled = !enabled;

            if (handGrabInteractable != null)
                handGrabInteractable.enabled = enabled;

            if (_grabbable != null)
                _grabbable.enabled = enabled;

            if (!enabled && _isGrabbed)
                ForceRelease();
        }

        private void ForceRelease()
        {
            if (handGrabInteractable != null)
            {
                _isGrabbed = false;
                OnReleased();
            }
            else
            {
                _isGrabbed = false;
            }
        }

        private void OnPickedUp()
        {
            if (_grabDisabled)
            {
                ForceRelease();
                return;
            }

            if (returnObjectHome != null)
            {
                returnObjectHome.CancelReturn();
                returnObjectHome.enabled = false;
            }

            if (_isSnapped)
                SafetyLog.Info($"PPESnapItem [{name}]: grabbed while snapped, monitoring pull distance.", this);
        }

        private void OnReleased()
        {
            if (_isSnapped) return;

            PPESnapSlot slot = FindSlotToSnap();
            if (slot != null && slot.TryAcceptSnap(this))
                SnapTo(slot);
            else if (returnObjectHome != null)
            {
                returnObjectHome.enabled = true;
                returnObjectHome.RequestReturn();
            }
            else
                SetPhysicsEnabled(true);
        }

        private PPESnapSlot FindSlotToSnap()
        {
            if (_hoveringSlot != null && !_hoveringSlot.IsOccupied && _hoveringSlot.Accepts(_ppeItem.ppeType))
                return _hoveringSlot;

            if (snapSearchRadius <= 0f) return null;

            Collider[] hits = Physics.OverlapSphere(transform.position, snapSearchRadius, snapSearchMask, QueryTriggerInteraction.Collide);
            PPESnapSlot best = null;
            float bestDistSq = float.PositiveInfinity;

            foreach (var hit in hits)
            {
                if (hit == null) continue;
                var slot = hit.GetComponentInParent<PPESnapSlot>();
                if (slot == null) continue;
                if (slot.IsOccupied) continue;
                if (!slot.Accepts(_ppeItem.ppeType)) continue;

                float d = (slot.transform.position - transform.position).sqrMagnitude;
                if (d < bestDistSq)
                {
                    best = slot;
                    bestDistSq = d;
                }
            }

            return best;
        }

        private void SnapTo(PPESnapSlot slot)
        {
            _currentSlot = slot;
            _isSnapped = true;
            CacheSnapOverrideLocalPose();
            ApplySnapPose(slot.transform.position, slot.transform.rotation, true);
            SetPhysicsEnabled(false);

            if (returnObjectHome != null)
                returnObjectHome.enabled = false;

            SafetyLog.Info($"PPESnapItem [{name}]: snapped to {slot.name}", this);
        }

        private void Unsnap()
        {
            if (_currentSlot != null)
                _currentSlot.OnItemUnsnapped(this);

            _currentSlot = null;
            _isSnapped = false;
            SetPhysicsEnabled(true);

            // If the user is still holding it, keep ReturnObjectHome disabled until released.
            if (returnObjectHome != null && !_isGrabbed)
            {
                returnObjectHome.enabled = true;
                returnObjectHome.RequestReturn();
            }
        }

        private void LateUpdate()
        {
            if (!_isSnapped || _currentSlot == null) return;

            if (_isGrabbed)
            {
                if (_currentSlot.IsLocked) return;

                float dist = Vector3.Distance(transform.position, _currentSlot.transform.position);
                if (dist >= unsnapDistance)
                {
                    Unsnap();
                    return;
                }
            }

            if (trackSnapPoseOverrideEveryFrame && snapPoseOverride != null)
                CacheSnapOverrideLocalPose();

            bool instant = followSpeed <= 0f;
            ApplySnapPose(_currentSlot.transform.position, _currentSlot.transform.rotation, instant);
        }

        private void CacheSnapOverrideLocalPose()
        {
            if (snapPoseOverride == null)
            {
                _useSnapPoseOverride = false;
                return;
            }

            _snapOverrideLocalPos = transform.InverseTransformPoint(snapPoseOverride.position);
            _snapOverrideLocalRot = Quaternion.Inverse(transform.rotation) * snapPoseOverride.rotation;
            _useSnapPoseOverride = true;
        }

        private void ApplySnapPose(Vector3 slotPosition, Quaternion slotRotation, bool instant)
        {
            Vector3 targetPos = slotPosition;
            Quaternion targetRot = slotRotation;

            if (_useSnapPoseOverride)
            {
                // Want: (rootRot * overrideLocalRot) == slotRot  => rootRot = slotRot * inverse(overrideLocalRot)
                targetRot = slotRotation * Quaternion.Inverse(_snapOverrideLocalRot);

                // Want: rootPos + rootRot * overrideLocalPos == slotPos  => rootPos = slotPos - rootRot * overrideLocalPos
                targetPos = slotPosition - (targetRot * _snapOverrideLocalPos);
            }

            if (instant || followSpeed <= 0f)
            {
                transform.SetPositionAndRotation(targetPos, targetRot);
                return;
            }

            float t = followSpeed * Time.deltaTime;
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPos, t),
                Quaternion.Slerp(transform.rotation, targetRot, t));
        }

        private void SetPhysicsEnabled(bool physicsEnabled)
        {
            _rigidbody.isKinematic = !physicsEnabled;
            _rigidbody.useGravity = physicsEnabled;

            if (!physicsEnabled)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
        }

        // Trigger detection — notifies slots directly
        private void OnTriggerEnter(Collider other)
        {
            var slot = other.GetComponentInParent<PPESnapSlot>();
            if (slot == null || !slot.Accepts(_ppeItem.ppeType)) return;
            if (slot.IsOccupied) return;

            _hoveringSlot = slot;
            slot.OnItemEntered(this);
        }

        private void OnTriggerExit(Collider other)
        {
            var slot = other.GetComponentInParent<PPESnapSlot>();
            if (slot == null) return;

            if (_hoveringSlot == slot)
                _hoveringSlot = null;

            slot.OnItemExited(this);
        }
    }
}
