using System.Collections;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Gameplay.Events;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Gameplay.PPE
{
    /// <summary>
    /// Orchestrates the retractable lanyard interaction for the safety harness.
    ///
    /// Lifecycle:
    ///   1. IDLE       — Lanyard tip is parented to the harness, rope invisible.
    ///   2. PULLING    — Player grabbed the tip; a lightweight LineRenderer stretches
    ///                   from the harness D-ring to the hand (no Verlet physics yet).
    ///   3. LOCKED     — Released near an <see cref="AnchorPoint"/>; Verlet rope spawns
    ///                   at 1.5 m between harness and anchor. Emits ActionAttempt.
    ///   4. RETRACTING — Released in open air (no anchor nearby); waits briefly,
    ///                   then the tip smoothly returns to the harness and re-parents.
    ///
    /// Hierarchy setup:
    ///   HarnessRoot (PPEItem, PPESnapItem — snapped on body)
    ///     └─ LanyardTip (this script + Grabbable + HandGrabInteractable + Rigidbody)
    ///          └─ (optional small mesh: carabiner / snap hook visual)
    ///
    /// The VerletLanyard component lives on the same GameObject (auto-added if missing).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RetractableLanyardController : MonoBehaviour
    {
        // ── State ─────────────────────────────────────────────────

        public enum LanyardState { Idle, Pulling, Locked, Retracting }

        [Header("Current State (read-only)")]
        [SerializeField] private LanyardState state = LanyardState.Idle;
        public LanyardState State => state;

        // ── References ────────────────────────────────────────────

        [Header("Anchor References")]
        [Tooltip("Transform on the harness where the lanyard originates (D-ring on back/chest).")]
        [SerializeField] private Transform harnessAttachPoint;

        [Header("Idle Follow")]
        [Tooltip("Local offset from harnessAttachPoint where the tip rests when idle.")]
        [SerializeField] private Vector3 idlePositionOffset = Vector3.zero;

        [Tooltip("Local rotation offset from harnessAttachPoint when idle.")]
        [SerializeField] private Vector3 idleRotationOffset = Vector3.zero;

        [Tooltip("How fast the tip follows the harness attach point. 0 = instant (snap).")]
        [SerializeField, Range(0f, 30f)] private float idleFollowSpeed = 20f;

        [Header("Grab Detection")]
        [Tooltip("Grabbable on the lanyard tip. Auto-found if null.")]
        [SerializeField] private Grabbable grabbable;

        [Tooltip("HandGrabInteractable on the tip. Auto-found if null.")]
        [SerializeField] private HandGrabInteractable handGrabInteractable;

        [Header("Anchor Detection")]
        [Tooltip("Radius to search for AnchorPoint components on release.")]
        [SerializeField, Range(0.05f, 0.5f)] private float anchorSearchRadius = 0.2f;

        [Tooltip("Layer mask for anchor point detection. Use Default if unsure.")]
        [SerializeField] private LayerMask anchorLayerMask = ~0;

        [Header("Rope Settings")]
        [Tooltip("Locked rope length when connected to the olhal de ancoragem (NR-35).")]
        [SerializeField, Range(0.5f, 2f)] private float lockedRopeLength = 1.5f;

        [Header("Retract Behavior")]
        [Tooltip("Seconds the rope stays visible at release length before retracting.")]
        [SerializeField, Range(0f, 3f)] private float retractDelay = 1.5f;

        [Tooltip("Seconds for the tip to travel back to the harness.")]
        [SerializeField, Range(0.1f, 1.5f)] private float retractDuration = 0.6f;

        [Tooltip("Ease curve for the retract animation.")]
        [SerializeField] private AnimationCurve retractCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Action Integration")]
        [Tooltip("ActionType SO for 'connect_harness'. If set, emits ActionAttempt on lock.")]
        [SerializeField] private ActionTypeSO connectHarnessAction;

        [Tooltip("ActionId fallback if no SO is assigned.")]
        [SerializeField] private string connectActionId = "connect_harness";

        [Header("Events")]
        [Tooltip("Fired when the lanyard locks to an anchor. Bool = isCorrectAnchor.")]
        public UnityEvent<bool> onLanyardLocked;

        [Tooltip("Fired when the lanyard returns to idle.")]
        public UnityEvent onLanyardRetracted;

        // ── Private ───────────────────────────────────────────────

        private VerletLanyard _verletLanyard;
        private Rigidbody _rb;
        private LineRenderer _lineRenderer;

        private bool _isGrabbed;
        private Coroutine _retractCoroutine;
        private AnchorPoint _lockedAnchor;

        // Reusable buffer for OverlapSphere (no alloc per frame)
        private readonly Collider[] _overlapBuffer = new Collider[16];

        // ── Unity Lifecycle ───────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            if (grabbable == null)
                grabbable = GetComponent<Grabbable>();
            if (handGrabInteractable == null)
                handGrabInteractable = GetComponentInChildren<HandGrabInteractable>();

            // Ensure VerletLanyard exists
            _verletLanyard = GetComponent<VerletLanyard>();
            if (_verletLanyard == null)
                _verletLanyard = gameObject.AddComponent<VerletLanyard>();

            _lineRenderer = GetComponent<LineRenderer>();
        }

        private void Start()
        {
            if (harnessAttachPoint == null)
            {
                SafetyLog.Warning(
                    "RetractableLanyardController: harnessAttachPoint não atribuído! " +
                    "Usando parent transform como fallback.", this);
                harnessAttachPoint = transform.parent;
            }

            // Subscribe to grab events
            if (grabbable != null)
                grabbable.WhenPointerEventRaised += OnPointerEvent;
            else if (handGrabInteractable != null)
                handGrabInteractable.WhenStateChanged += OnGrabStateChanged;
            else
                SafetyLog.Error("RetractableLanyardController: No Grabbable or HandGrabInteractable found!", this);

            // Start in idle
            EnterIdle();
        }

        private void OnDestroy()
        {
            if (grabbable != null)
                grabbable.WhenPointerEventRaised -= OnPointerEvent;
            if (handGrabInteractable != null)
                handGrabInteractable.WhenStateChanged -= OnGrabStateChanged;
        }

        private void LateUpdate()
        {
            switch (state)
            {
                case LanyardState.Idle:
                    FollowHarnessAttachPoint();
                    break;
                case LanyardState.Pulling:
                    UpdatePullingVisual();
                    break;
            }
        }

        /// <summary>
        /// Smoothly follows the harnessAttachPoint transform with the configured offset.
        /// Same pattern as PPESnapItem following its PPESnapSlot.
        /// </summary>
        private void FollowHarnessAttachPoint()
        {
            if (harnessAttachPoint == null) return;

            Quaternion offsetRot = Quaternion.Euler(idleRotationOffset);
            Vector3 targetPos = harnessAttachPoint.TransformPoint(idlePositionOffset);
            Quaternion targetRot = harnessAttachPoint.rotation * offsetRot;

            if (idleFollowSpeed <= 0f)
            {
                // Instant snap
                transform.SetPositionAndRotation(targetPos, targetRot);
            }
            else
            {
                float t = Mathf.Clamp01(idleFollowSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, targetPos, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
            }
        }

        // ── Grab Detection ────────────────────────────────────────

        private void OnPointerEvent(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select)
                OnGrabbed();
            else if (evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
                OnReleased();
        }

        private void OnGrabStateChanged(InteractableStateChangeArgs args)
        {
            bool wasGrabbed = _isGrabbed;
            _isGrabbed = args.NewState == InteractableState.Select;

            if (!wasGrabbed && _isGrabbed)
                OnGrabbed();
            else if (wasGrabbed && !_isGrabbed)
                OnReleased();
        }

        // ── State Transitions ─────────────────────────────────────

        private void OnGrabbed()
        {
            _isGrabbed = true;

            // Cancel any ongoing retract
            if (_retractCoroutine != null)
            {
                StopCoroutine(_retractCoroutine);
                _retractCoroutine = null;
            }

            if (state == LanyardState.Locked)
            {
                // Player is pulling the tip out of a locked anchor — unlock
                Unlock();
            }

            EnterPulling();
        }

        private void OnReleased()
        {
            _isGrabbed = false;

            if (state != LanyardState.Pulling) return;

            // Search for a nearby AnchorPoint
            AnchorPoint anchor = FindNearestAnchor();

            if (anchor != null)
            {
                EnterLocked(anchor);
            }
            else
            {
                EnterRetracting();
            }
        }

        // ── IDLE ──────────────────────────────────────────────────

        private void EnterIdle()
        {
            state = LanyardState.Idle;

            // Disable Verlet physics
            _verletLanyard.enabled = false;

            // Hide LineRenderer
            if (_lineRenderer != null)
                _lineRenderer.enabled = false;

            // Un-parent so the tip lives at scene root — follow logic in LateUpdate
            // keeps it glued to harnessAttachPoint without inheriting the harness Grabbable chain.
            transform.SetParent(null);

            // Snap to harness immediately (no lerp pop on first frame)
            if (harnessAttachPoint != null)
            {
                Quaternion offsetRot = Quaternion.Euler(idleRotationOffset);
                transform.SetPositionAndRotation(
                    harnessAttachPoint.TransformPoint(idlePositionOffset),
                    harnessAttachPoint.rotation * offsetRot);
            }

            // Kinematic while idle (no physics jitter)
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;

            _lockedAnchor = null;
        }

        // ── PULLING ───────────────────────────────────────────────

        private void EnterPulling()
        {
            state = LanyardState.Pulling;

            // Un-parent so the hand can move it freely
            transform.SetParent(null);

            // Enable physics for hand tracking
            _rb.isKinematic = false;

            // Disable Verlet (we use a simple 2-point line while pulling)
            _verletLanyard.enabled = false;

            // Enable LineRenderer for the simple stretch visual
            if (_lineRenderer != null)
            {
                _lineRenderer.enabled = true;
                _lineRenderer.positionCount = 2;
            }

            SafetyLog.Info("VerletLanyard: Pulling — player grabbed lanyard tip.", this);
        }

        private void UpdatePullingVisual()
        {
            if (_lineRenderer == null || harnessAttachPoint == null) return;

            // Simple 2-point line: harness → hand (tip position)
            _lineRenderer.SetPosition(0, harnessAttachPoint.position);
            _lineRenderer.SetPosition(1, transform.position);
        }

        // ── LOCKED ────────────────────────────────────────────────

        private void EnterLocked(AnchorPoint anchor)
        {
            state = LanyardState.Locked;
            _lockedAnchor = anchor;

            // Disable the simple pulling line — Verlet takes over
            if (_lineRenderer != null)
                _lineRenderer.positionCount = 0;

            // Snap tip to anchor attach point
            transform.SetParent(null);
            transform.position = anchor.AttachPosition;
            _rb.isKinematic = true;
            _rb.linearVelocity = Vector3.zero;

            // Configure and enable Verlet rope
            _verletLanyard.SetStartAnchor(harnessAttachPoint);
            _verletLanyard.SetEndAnchor(anchor.AttachTransform);
            _verletLanyard.RopeLength = lockedRopeLength;
            _verletLanyard.enabled = true;

            // Emit action attempt for the task system
            EmitConnectAction(anchor);

            // Notify listeners
            onLanyardLocked?.Invoke(anchor.isCorrectAnchor);

            SafetyLog.Info(
                $"VerletLanyard: Locked to '{anchor.name}' " +
                $"(correct={anchor.isCorrectAnchor}) @ {lockedRopeLength:F1}m.", this);
        }

        /// <summary>
        /// Disconnect the lanyard from its current anchor (e.g. player pulls it out).
        /// </summary>
        public void Unlock()
        {
            if (state != LanyardState.Locked) return;

            _verletLanyard.SetEndAnchor(null);
            _verletLanyard.enabled = false;
            _lockedAnchor = null;

            SafetyLog.Info("VerletLanyard: Unlocked from anchor.", this);
        }

        // ── RETRACTING ────────────────────────────────────────────

        private void EnterRetracting()
        {
            state = LanyardState.Retracting;

            // Keep the pulling line visible briefly, then retract
            _retractCoroutine = StartCoroutine(RetractSequence());
        }

        private IEnumerator RetractSequence()
        {
            // Phase 1: Hold at current length for a beat
            yield return new WaitForSeconds(retractDelay);

            // Phase 2: Smoothly move tip back to harness attach point
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            _rb.isKinematic = true;
            _rb.linearVelocity = Vector3.zero;

            Quaternion offsetRot = Quaternion.Euler(idleRotationOffset);
            float elapsed = 0f;

            while (elapsed < retractDuration)
            {
                elapsed += Time.deltaTime;
                float t = retractCurve.Evaluate(Mathf.Clamp01(elapsed / retractDuration));

                // Recompute target every frame — harness moves with the player
                Vector3 targetPos = harnessAttachPoint != null
                    ? harnessAttachPoint.TransformPoint(idlePositionOffset)
                    : startPos;
                Quaternion targetRot = harnessAttachPoint != null
                    ? harnessAttachPoint.rotation * offsetRot
                    : startRot;

                transform.position = Vector3.Lerp(startPos, targetPos, t);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

                // Update the simple line to shrink with the tip
                if (_lineRenderer != null && _lineRenderer.enabled && harnessAttachPoint != null)
                {
                    _lineRenderer.SetPosition(0, harnessAttachPoint.position);
                    _lineRenderer.SetPosition(1, transform.position);
                }

                yield return null;
            }

            _retractCoroutine = null;

            // Back to idle
            EnterIdle();
            onLanyardRetracted?.Invoke();

            SafetyLog.Info("VerletLanyard: Retracted back to harness.", this);
        }

        // ── Anchor Detection ──────────────────────────────────────

        private AnchorPoint FindNearestAnchor()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, anchorSearchRadius, _overlapBuffer,
                anchorLayerMask, QueryTriggerInteraction.Collide);

            AnchorPoint best = null;
            float bestDistSq = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                var ap = _overlapBuffer[i].GetComponentInParent<AnchorPoint>();
                if (ap == null) continue;

                float distSq = (ap.AttachPosition - transform.position).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    best = ap;
                    bestDistSq = distSq;
                }
            }

            return best;
        }

        // ── Action Emission ───────────────────────────────────────

        private void EmitConnectAction(AnchorPoint anchor)
        {
            if (EventBus.Instance == null) return;

            string actionId = connectHarnessAction != null
                ? connectHarnessAction.ActionId
                : connectActionId;

            string context = anchor.isCorrectAnchor
                ? "correct_anchor_olhal"
                : "incorrect_anchor_montante";

            ActionEvents.PublishActionAttempt(
                actionId,
                sourceId: name,
                context: context,
                position: anchor.AttachPosition);
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>
        /// Force-disconnect and return to idle. Useful for session reset.
        /// </summary>
        public void ForceReset()
        {
            if (_retractCoroutine != null)
            {
                StopCoroutine(_retractCoroutine);
                _retractCoroutine = null;
            }

            Unlock();
            EnterIdle();
        }

        /// <summary>
        /// Whether the lanyard is currently locked to an anchor.
        /// </summary>
        public bool IsLocked => state == LanyardState.Locked;

        /// <summary>
        /// Whether the lanyard is locked to the CORRECT anchor (olhal de ancoragem).
        /// </summary>
        public bool IsLockedCorrectly => IsLocked && _lockedAnchor != null && _lockedAnchor.isCorrectAnchor;

        // ── Gizmos ────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Show anchor search radius
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, anchorSearchRadius);

            // Show follow target (harness D-ring)
            if (harnessAttachPoint != null)
            {
                Vector3 idleWorld = harnessAttachPoint.TransformPoint(idlePositionOffset);
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(idleWorld, 0.02f);
                Gizmos.DrawLine(transform.position, idleWorld);
            }
        }
#endif
    }
}