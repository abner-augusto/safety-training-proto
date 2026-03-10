using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using SafetyProto.Gameplay.Interactables;
using SafetyProto.Core.Logging;
using SafetyProto.Data.Enums;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
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
            // Prefer Grabbable pointer events because they fire for any grab mode (hand grab, distance grab, etc).
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

            // WhenStateChanged fires whenever the interactable's state changes
            // (Normal → Hover → Select → Normal)
            // Verify this event name exists in your SDK version by checking
            // HandGrabInteractable.cs — it inherits from Interactable<> which
            // defines WhenStateChanged in Interactable.cs
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

        private void OnPickedUp()
        {
            // Prevent ReturnObjectHome from receiving the upcoming Unselect event; we'll decide what to do on release.
            if (returnObjectHome != null)
            {
                returnObjectHome.CancelReturn();
                returnObjectHome.enabled = false;
            }

            if (_isSnapped)
            {
                // Keep snapped until pull threshold is reached — handled in LateUpdate
                SafetyLog.Info($"PPESnapItem [{name}]: grabbed while snapped, monitoring pull distance.", this);
            }
        }

        private void OnReleased()
        {
            if (_isSnapped) return; // Already snapped, release doesn't unsnap

            // Try to snap to a nearby compatible slot.
            PPESnapSlot slot = FindSlotToSnap();
            if (slot != null && slot.TryAcceptSnap(this))
            {
                SnapTo(slot);
            }
            else
            {
                // No slot — either return home (if configured) or revert to normal physics.
                if (returnObjectHome != null)
                {
                    returnObjectHome.enabled = true;
                    returnObjectHome.RequestReturn();
                }
                else
                {
                    SetPhysicsEnabled(true);
                }
            }
        }

        private PPESnapSlot FindSlotToSnap()
        {
            if (_hoveringSlot != null && !_hoveringSlot.IsOccupied && _hoveringSlot.Accepts(_ppeItem.ppeType))
                return _hoveringSlot;

            if (snapSearchRadius <= 0f) return null;

            Collider[] hits = Physics.OverlapSphere(transform.position, snapSearchRadius, ~0, QueryTriggerInteraction.Collide);
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
            transform.SetPositionAndRotation(slot.transform.position, slot.transform.rotation);
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

            SafetyLog.Info($"PPESnapItem [{name}]: unsnapped.", this);
        }

        private void LateUpdate()
        {
            if (_isSnapped && _currentSlot != null)
            {
                if (_isGrabbed)
                {
                    // Check if hand has pulled far enough to unsnap
                    float dist = Vector3.Distance(transform.position, _currentSlot.transform.position);
                    if (dist >= unsnapDistance)
                    {
                        Unsnap();
                        return;
                    }
                }

                // Follow slot transform
                if (followSpeed <= 0f)
                {
                    transform.SetPositionAndRotation(
                        _currentSlot.transform.position,
                        _currentSlot.transform.rotation);
                }
                else
                {
                    transform.SetPositionAndRotation(
                        Vector3.Lerp(transform.position, _currentSlot.transform.position, followSpeed * Time.deltaTime),
                        Quaternion.Slerp(transform.rotation, _currentSlot.transform.rotation, followSpeed * Time.deltaTime));
                }
            }
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
