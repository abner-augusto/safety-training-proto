using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Runtime.Feedback;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Runtime.Scaffolding
{
    /// <summary>
    /// Validates and completes scaffold piece installation after Meta Interaction SDK hand grabs.
    ///
    /// Setup:
    ///   - SingleSocket: one HandGrabInteractable on the root object, one HandGrabPose child.
    ///   - TwoSockets:   one HandGrabInteractable on the root object, two HandGrabPose children
    ///                   (Handedness Left and Right). Two-hand detection uses
    ///                   Grabbable.SelectingPointsCount.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Grabbable))]
    public class ScaffoldPieceInstaller : MonoBehaviour
    {
        public enum InstallMode { SingleSocket, TwoSockets }

        // ── Action ───────────────────────────────────────────────

        [Header("Action")]
        [SerializeField] private ActionTypeSO installedAction;
        [SerializeField] private string actionIdOverride = string.Empty;
        [SerializeField] private string sourceId = string.Empty;
        [SerializeField] private string context = "scaffold_install";
        [SerializeField] private int interactorId;

        // ── Install Mode ─────────────────────────────────────────

        [Header("Install Mode")]
        [SerializeField] private InstallMode installMode = InstallMode.SingleSocket;
        [Tooltip("TwoSockets only: requires both hands to be holding at the moment of release.")]
        [SerializeField] private bool requireTwoHandsForTwoSockets = true;

        // ── Anchors & Sockets ────────────────────────────────────

        [Header("Piece Anchors")]
        [Tooltip("Anchor on the piece that aligns to Socket A. The only required field for SingleSocket.")]
        [SerializeField] private Transform pieceAnchorA;
        [Tooltip("Second anchor on the piece. Required only for TwoSockets.")]
        [SerializeField] private Transform pieceAnchorB;

        [Header("Target Sockets")]
        [SerializeField] private Transform targetSocketA;
        [SerializeField] private Transform targetSocketB;

        // ── Tolerances ───────────────────────────────────────────

        [Header("Tolerances")]
        [SerializeField] private float positionTolerance = 0.12f;
        [SerializeField] private float angleTolerance = 20f;

        // ── Snap & Lock ──────────────────────────────────────────

        [Header("Snap")]
        [Tooltip("Move into the final installed pose when released within tolerance.")]
        [SerializeField] private bool snapOnRelease = true;
        [Tooltip("Locks the piece after a successful install.")]
        [SerializeField] private bool lockAfterInstalled = true;
        [Tooltip("Disables grabbing after install.")]
        [SerializeField] private bool disableGrabAfterInstalled = true;
        [Tooltip("Disables non-trigger colliders after install to prevent physics overlap artifacts.")]
        [SerializeField] private bool disableCollidersAfterInstalled = true;

        // ── SDK References ───────────────────────────────────────

        [Header("Meta SDK References")]
        [Tooltip("Grabbable on the object. Auto-found if empty.")]
        [SerializeField] private Grabbable grabbable;
        [Tooltip("HandGrabInteractable on the object. Auto-found if empty.")]
        [SerializeField] private HandGrabInteractable handGrabInteractable;
        [Tooltip("ReturnObjectHome on the object. Auto-found if present.")]
        [SerializeField] private ReturnObjectHome returnHome;

        // ── Visual Feedback ──────────────────────────────────────

        [Header("Visual Feedback")]
        [Tooltip("Optional renderer on the real piece. Used for local feedback while held.")]
        [SerializeField] private Renderer pieceFeedbackRenderer;
        [Tooltip("Renderer of the ghost/preview on the scaffold slot. Disabled after install.")]
        [SerializeField] private Renderer targetPreviewRenderer;
        [Tooltip("Hides the slot ghost until the piece is being held.")]
        [SerializeField] private bool showTargetPreviewOnlyWhileGrabbed = true;
        [Tooltip("Disables the Target Preview Renderer after a successful install.")]
        [SerializeField] private bool hideTargetPreviewAfterInstalled = true;
        [SerializeField] private Color idleColor    = new Color(1f,   1f,   1f,  0.12f);
        [SerializeField] private Color validColor   = new Color(0.2f, 0.85f, 0.35f, 0.45f);
        [SerializeField] private Color invalidColor = new Color(1f,   0.45f, 0.2f,  0.35f);

        // ── Events ───────────────────────────────────────────────

        [Header("Events")]
        public UnityEvent onEnteredValidPose;
        public UnityEvent onExitedValidPose;
        public UnityEvent onInstalled;
        public UnityEvent onInvalidRelease;

        // ── Private state ────────────────────────────────────────

        private Rigidbody  _rigidbody;
        private Collider[] _colliders;
        private Material   _pieceFeedbackMaterial;
        private Material   _targetPreviewMaterial;
        private bool _isGrabbed;
        private bool _isInstalled;
        private bool _wasValidPose;
        private bool _reachedTwoHands;

        // ── Public API ───────────────────────────────────────────

        public bool IsInstalled        => _isInstalled;
        public bool IsValidInstallPose => HasValidInstallPose();

        /// <summary>How many hands are currently holding the piece.</summary>
        public int SelectingHandCount  => grabbable != null ? grabbable.SelectingPointsCount : 0;

        // ── Unity lifecycle ──────────────────────────────────────

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (grabbable == null)
                grabbable = GetComponent<Grabbable>();

            if (handGrabInteractable == null)
                handGrabInteractable = GetComponentInChildren<HandGrabInteractable>(includeInactive: true);

            if (returnHome == null)
                returnHome = GetComponent<ReturnObjectHome>();

            _colliders = GetComponentsInChildren<Collider>(includeInactive: false);

            if (pieceFeedbackRenderer != null)
                _pieceFeedbackMaterial = pieceFeedbackRenderer.material;

            if (targetPreviewRenderer != null)
                _targetPreviewMaterial = targetPreviewRenderer.material;

            SetFeedbackColor(idleColor);
            UpdateTargetPreviewVisibility();
        }

        private void OnEnable()
        {
            if (grabbable != null)
                grabbable.WhenPointerEventRaised += OnPointerEvent;
        }

        private void OnDisable()
        {
            if (grabbable != null)
                grabbable.WhenPointerEventRaised -= OnPointerEvent;
        }

        private void OnDestroy()
        {
            if (_pieceFeedbackMaterial != null) Destroy(_pieceFeedbackMaterial);
            if (_targetPreviewMaterial != null) Destroy(_targetPreviewMaterial);
        }

        private void LateUpdate()
        {
            if (_isInstalled) return;

            bool valid = HasValidInstallPose();
            if (valid != _wasValidPose)
            {
                _wasValidPose = valid;
                if (valid) onEnteredValidPose?.Invoke();
                else       onExitedValidPose?.Invoke();
            }

            SetFeedbackColor(_isGrabbed ? (valid ? validColor : invalidColor) : idleColor);
            UpdateTargetPreviewVisibility();
        }

        // ── Public methods ───────────────────────────────────────

        public void TryInstall()
        {
            if (_isInstalled) return;

            if (!HasEnoughHandsForInstall() || !HasValidInstallPose())
            {
                onInvalidRelease?.Invoke();
                return;
            }

            Install();
        }

        public void ResetInstall()
        {
            _isInstalled  = false;
            _wasValidPose = false;
            if (disableCollidersAfterInstalled)
                SetCollidersEnabled(true);
            SetPhysicsEnabled(true);
            SetGrabEnabled(true);
            if (returnHome != null) returnHome.enabled = true;
            SetFeedbackColor(idleColor);
            UpdateTargetPreviewVisibility();
        }

        // ── Grab events ──────────────────────────────────────────

        private void OnPointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    _isGrabbed = true;
                    SetPhysicsEnabled(true);
                    if (installMode == InstallMode.TwoSockets && requireTwoHandsForTwoSockets
                        && grabbable.SelectingPointsCount >= 2)
                        _reachedTwoHands = true;
                    UpdateTargetPreviewVisibility();
                    break;

                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    // Update _isGrabbed based on hands still present
                    _isGrabbed = grabbable.SelectingPointsCount > 0;

                    // Attempt install only when the last hand releases
                    if (!_isGrabbed)
                    {
                        TryInstall();
                        _reachedTwoHands = false;
                    }

                    UpdateTargetPreviewVisibility();
                    break;
            }
        }

        // ── Install logic ────────────────────────────────────────

        private void Install()
        {
            if (snapOnRelease)
                ApplySocketPose();

            _isInstalled = true;

            if (returnHome != null)
            {
                returnHome.CancelReturn();
                returnHome.enabled = false;
            }

            if (lockAfterInstalled)
                SetPhysicsEnabled(false);

            if (disableGrabAfterInstalled)
                SetGrabEnabled(false);

            if (disableCollidersAfterInstalled)
                SetCollidersEnabled(false);

            SetFeedbackColor(validColor);

            if (hideTargetPreviewAfterInstalled && targetPreviewRenderer != null)
                targetPreviewRenderer.enabled = false;

            PublishInstalledAction();
            onInstalled?.Invoke();
        }

        private bool HasEnoughHandsForInstall()
        {
            if (installMode != InstallMode.TwoSockets || !requireTwoHandsForTwoSockets)
                return true;

            return _reachedTwoHands;
        }

        private bool HasValidInstallPose()
        {
            if (!HasRequiredReferences()) return false;

            bool aValid = IsAnchorAligned(pieceAnchorA, targetSocketA);
            if (installMode == InstallMode.SingleSocket) return aValid;

            return aValid && IsAnchorAligned(pieceAnchorB, targetSocketB);
        }

        private bool HasRequiredReferences()
        {
            if (pieceAnchorA == null || targetSocketA == null) return false;
            if (installMode == InstallMode.TwoSockets)
                return pieceAnchorB != null && targetSocketB != null;
            return true;
        }

        private bool IsAnchorAligned(Transform pieceAnchor, Transform socket)
        {
            if (Vector3.Distance(pieceAnchor.position, socket.position) > positionTolerance)
                return false;

            return Quaternion.Angle(pieceAnchor.rotation, socket.rotation) <= angleTolerance;
        }

        private void ApplySocketPose()
        {
            if (pieceAnchorA == null || targetSocketA == null) return;

            Vector3    anchorLocalPos = transform.InverseTransformPoint(pieceAnchorA.position);
            Quaternion anchorLocalRot = Quaternion.Inverse(transform.rotation) * pieceAnchorA.rotation;

            Quaternion targetRootRot = targetSocketA.rotation * Quaternion.Inverse(anchorLocalRot);
            Vector3    targetRootPos = targetSocketA.position - targetRootRot * anchorLocalPos;

            transform.SetPositionAndRotation(targetRootPos, targetRootRot);
        }

        // ── Helpers ──────────────────────────────────────────────

        private void PublishInstalledAction()
        {
            var actionId = GetConfiguredActionId();
            if (string.IsNullOrEmpty(actionId))
            {
                SafetyLog.Warning($"[ScaffoldPieceInstaller] Nenhum ActionId configurado em {name}. Instalação concluída sem evento de tarefa.", this);
                return;
            }

            var resolvedSource  = string.IsNullOrWhiteSpace(sourceId) ? gameObject.name : sourceId.Trim();
            var resolvedContext = string.IsNullOrWhiteSpace(context)  ? null            : context.Trim();

            ActionEvents.PublishActionAttempt(actionId, resolvedSource, resolvedContext, transform.position, interactorId);
            SafetyLog.Info($"[ScaffoldPieceInstaller] '{name}' instalado — ActionAttempt '{actionId}' emitido.", this);
        }

        private void SetPhysicsEnabled(bool physicsEnabled)
        {
            if (_rigidbody == null) return;

            if (!physicsEnabled)
            {
                _rigidbody.linearVelocity  = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }

            _rigidbody.isKinematic = !physicsEnabled;
            _rigidbody.useGravity  =  physicsEnabled;
        }

        private void SetCollidersEnabled(bool enabled)
        {
            if (_colliders == null) return;
            foreach (var col in _colliders)
                if (col != null && !col.isTrigger) col.enabled = enabled;
        }

        private void SetGrabEnabled(bool enabled)
        {
            if (grabbable != null)
                grabbable.enabled = enabled;

            if (handGrabInteractable != null)
                handGrabInteractable.enabled = enabled;
        }

        private void SetFeedbackColor(Color color)
        {
            if (_pieceFeedbackMaterial  != null) _pieceFeedbackMaterial.color  = color;
            if (_targetPreviewMaterial  != null) _targetPreviewMaterial.color  = color;
        }

        private void UpdateTargetPreviewVisibility()
        {
            if (targetPreviewRenderer == null || _isInstalled) return;
            targetPreviewRenderer.enabled = !showTargetPreviewOnlyWhileGrabbed || _isGrabbed;
        }

        private string GetConfiguredActionId()
        {
            if (installedAction != null && !string.IsNullOrWhiteSpace(installedAction.ActionId))
                return installedAction.ActionId.Trim();

            return string.IsNullOrWhiteSpace(actionIdOverride) ? string.Empty : actionIdOverride.Trim();
        }

        // ── Editor ───────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (installedAction != null && !string.IsNullOrWhiteSpace(installedAction.ActionId))
                actionIdOverride = installedAction.ActionId;
            else if (!string.IsNullOrEmpty(actionIdOverride))
                actionIdOverride = actionIdOverride.Trim();

            if (!string.IsNullOrEmpty(sourceId)) sourceId = sourceId.Trim();
            if (!string.IsNullOrEmpty(context))  context  = context.Trim();

            positionTolerance = Mathf.Max(0f, positionTolerance);
            angleTolerance    = Mathf.Clamp(angleTolerance, 0f, 180f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = HasValidInstallPose() ? Color.green : Color.yellow;
            if (pieceAnchorA != null) Gizmos.DrawWireSphere(pieceAnchorA.position, 0.035f);
            if (pieceAnchorB != null) Gizmos.DrawWireSphere(pieceAnchorB.position, 0.035f);

            Gizmos.color = Color.cyan;
            if (targetSocketA != null) Gizmos.DrawWireSphere(targetSocketA.position, positionTolerance);
            if (targetSocketB != null) Gizmos.DrawWireSphere(targetSocketB.position, positionTolerance);
        }
#endif
    }
}