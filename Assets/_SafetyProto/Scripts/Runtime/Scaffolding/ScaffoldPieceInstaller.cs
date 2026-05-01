using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Runtime.Scaffolding
{
    /// <summary>
    /// Validates and completes scaffold piece installation after Meta Interaction SDK hand grabs.
    /// Configure one socket for short pieces, or two sockets for long pieces such as toe boards.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ScaffoldPieceInstaller : MonoBehaviour
    {
        public enum InstallMode
        {
            SingleSocket,
            TwoSockets
        }

        [Header("Action")]
        [SerializeField] private ActionTypeSO installedAction;
        [SerializeField] private string actionIdOverride = string.Empty;
        [SerializeField] private string sourceId = string.Empty;
        [SerializeField] private string context = "scaffold_install";
        [SerializeField] private int interactorId;

        [Header("Install Mode")]
        [SerializeField] private InstallMode installMode = InstallMode.SingleSocket;
        [Tooltip("For long pieces, require two handles to be grabbed at the same time before installation can complete.")]
        [SerializeField] private bool requireTwoHandsForTwoSockets = true;

        [Header("Piece Anchors")]
        [Tooltip("Anchor on the piece that aligns to Target Socket A. For SingleSocket this is the only required anchor.")]
        [SerializeField] private Transform pieceAnchorA;
        [Tooltip("Second anchor on the piece. Required only for TwoSockets.")]
        [SerializeField] private Transform pieceAnchorB;

        [Header("Target Sockets")]
        [SerializeField] private Transform targetSocketA;
        [SerializeField] private Transform targetSocketB;

        [Header("Tolerances")]
        [SerializeField] private float positionTolerance = 0.12f;
        [SerializeField] private float angleTolerance = 20f;

        [Header("Snap")]
        [Tooltip("Move into the final installed pose when released inside tolerance.")]
        [SerializeField] private bool snapOnRelease = true;
        [Tooltip("Keep the piece locked after a successful install.")]
        [SerializeField] private bool lockAfterInstalled = true;
        [Tooltip("Disable configured grab handles after install.")]
        [SerializeField] private bool disableGrabAfterInstalled = true;

        [Header("Meta SDK Grab References")]
        [Tooltip("Explicit grab handles. Put HandGrabInteractable components only on the visible handles, not the full 3m piece.")]
        [SerializeField] private HandGrabInteractable[] grabHandles;
        [Tooltip("Optional root Grabbable. Used as a fallback release signal if no handles are assigned.")]
        [SerializeField] private Grabbable grabbable;

        [Header("Visual Feedback")]
        [Tooltip("Optional renderer on the real piece. Used only for local feedback while grabbed.")]
        [SerializeField] private Renderer pieceFeedbackRenderer;
        [Tooltip("Renderer for the installed ghost/preview placed on the scaffold slot. Disabled after install.")]
        [SerializeField] private Renderer targetPreviewRenderer;
        [Tooltip("Keep the slot ghost hidden until this piece is being grabbed.")]
        [SerializeField] private bool showTargetPreviewOnlyWhileGrabbed = true;
        [Tooltip("Disable Target Preview Renderer after a successful install.")]
        [SerializeField] private bool hideTargetPreviewAfterInstalled = true;
        [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.12f);
        [SerializeField] private Color validColor = new Color(0.2f, 0.85f, 0.35f, 0.45f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.45f, 0.2f, 0.35f);

        [Header("Events")]
        public UnityEvent onEnteredValidPose;
        public UnityEvent onExitedValidPose;
        public UnityEvent onInstalled;
        public UnityEvent onInvalidRelease;

        private readonly List<HandGrabInteractable> _selectedHandles = new List<HandGrabInteractable>(2);
        private readonly Dictionary<HandGrabInteractable, Action<InteractableStateChangeArgs>> _handleCallbacks =
            new Dictionary<HandGrabInteractable, Action<InteractableStateChangeArgs>>();
        private Rigidbody _rigidbody;
        private Material _pieceFeedbackMaterial;
        private Material _targetPreviewMaterial;
        private bool _isGrabbed;
        private bool _isInstalled;
        private bool _wasValidPose;
        private bool _hadRequiredHandsThisGrab;

        public bool IsInstalled => _isInstalled;
        public bool IsValidInstallPose => HasValidInstallPose();
        public int SelectedHandleCount => _selectedHandles.Count;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }

            if ((grabHandles == null || grabHandles.Length == 0))
            {
                grabHandles = GetComponentsInChildren<HandGrabInteractable>(includeInactive: true);
            }

            if (pieceFeedbackRenderer != null)
            {
                _pieceFeedbackMaterial = pieceFeedbackRenderer.material;
            }

            if (targetPreviewRenderer != null)
            {
                _targetPreviewMaterial = targetPreviewRenderer.material;
            }

            SetFeedbackColor(idleColor);
            UpdateTargetPreviewVisibility();
        }

        private void OnEnable()
        {
            SubscribeGrabEvents();
        }

        private void OnDisable()
        {
            UnsubscribeGrabEvents();
        }

        private void OnDestroy()
        {
            if (_pieceFeedbackMaterial != null)
            {
                Destroy(_pieceFeedbackMaterial);
            }

            if (_targetPreviewMaterial != null)
            {
                Destroy(_targetPreviewMaterial);
            }
        }

        private void LateUpdate()
        {
            if (_isInstalled)
            {
                return;
            }

            bool valid = HasValidInstallPose();
            if (valid != _wasValidPose)
            {
                _wasValidPose = valid;
                if (valid)
                {
                    onEnteredValidPose?.Invoke();
                }
                else
                {
                    onExitedValidPose?.Invoke();
                }
            }

            if (_isGrabbed)
            {
                SetFeedbackColor(valid ? validColor : invalidColor);
            }
            else
            {
                SetFeedbackColor(idleColor);
            }

            UpdateTargetPreviewVisibility();
        }

        public void TryInstall()
        {
            if (_isInstalled)
            {
                return;
            }

            if (!HasEnoughHandsForInstall() || !HasValidInstallPose())
            {
                onInvalidRelease?.Invoke();
                return;
            }

            Install();
        }

        public void ResetInstall()
        {
            _isInstalled = false;
            _wasValidPose = false;
            SetPhysicsEnabled(true);
            SetGrabEnabled(true);
            SetFeedbackColor(idleColor);
            UpdateTargetPreviewVisibility();
        }

        private void Install()
        {
            if (snapOnRelease)
            {
                ApplySocketPose();
            }

            _isInstalled = true;

            if (lockAfterInstalled)
            {
                SetPhysicsEnabled(false);
            }

            if (disableGrabAfterInstalled)
            {
                SetGrabEnabled(false);
            }

            SetFeedbackColor(validColor);
            if (hideTargetPreviewAfterInstalled && targetPreviewRenderer != null)
            {
                targetPreviewRenderer.enabled = false;
            }
            PublishInstalledAction();
            onInstalled?.Invoke();
        }

        private void PublishInstalledAction()
        {
            var actionId = GetConfiguredActionId();
            if (string.IsNullOrEmpty(actionId))
            {
                SafetyLog.Warning($"[ScaffoldPieceInstaller] No ActionId configured on {name}. Install completed without task event.", this);
                return;
            }

            var resolvedSource = string.IsNullOrWhiteSpace(sourceId) ? gameObject.name : sourceId.Trim();
            var resolvedContext = string.IsNullOrWhiteSpace(context) ? null : context.Trim();
            ActionEvents.PublishActionAttempt(actionId, resolvedSource, resolvedContext, transform.position, interactorId);
            SafetyLog.Info($"[ScaffoldPieceInstaller] Installed '{name}' and emitted ActionAttempt '{actionId}'.", this);
        }

        private bool HasEnoughHandsForInstall()
        {
            if (installMode != InstallMode.TwoSockets || !requireTwoHandsForTwoSockets)
            {
                return true;
            }

            return _selectedHandles.Count >= 2 || _hadRequiredHandsThisGrab;
        }

        private bool HasValidInstallPose()
        {
            if (!HasRequiredReferences())
            {
                return false;
            }

            bool aValid = IsAnchorAligned(pieceAnchorA, targetSocketA);
            if (installMode == InstallMode.SingleSocket)
            {
                return aValid;
            }

            return aValid && IsAnchorAligned(pieceAnchorB, targetSocketB);
        }

        private bool HasRequiredReferences()
        {
            if (pieceAnchorA == null || targetSocketA == null)
            {
                return false;
            }

            if (installMode == InstallMode.TwoSockets)
            {
                return pieceAnchorB != null && targetSocketB != null;
            }

            return true;
        }

        private bool IsAnchorAligned(Transform pieceAnchor, Transform socket)
        {
            float distance = Vector3.Distance(pieceAnchor.position, socket.position);
            if (distance > positionTolerance)
            {
                return false;
            }

            float angle = Quaternion.Angle(pieceAnchor.rotation, socket.rotation);
            return angle <= angleTolerance;
        }

        private void ApplySocketPose()
        {
            if (pieceAnchorA == null || targetSocketA == null)
            {
                return;
            }

            Vector3 anchorLocalPos = transform.InverseTransformPoint(pieceAnchorA.position);
            Quaternion anchorLocalRot = Quaternion.Inverse(transform.rotation) * pieceAnchorA.rotation;

            Quaternion targetRootRot = targetSocketA.rotation * Quaternion.Inverse(anchorLocalRot);
            Vector3 targetRootPos = targetSocketA.position - targetRootRot * anchorLocalPos;
            transform.SetPositionAndRotation(targetRootPos, targetRootRot);
        }

        private void SubscribeGrabEvents()
        {
            if (grabHandles != null && grabHandles.Length > 0)
            {
                for (int i = 0; i < grabHandles.Length; i++)
                {
                    var handle = grabHandles[i];
                    if (handle != null && !_handleCallbacks.ContainsKey(handle))
                    {
                        Action<InteractableStateChangeArgs> callback = args => OnHandleStateChanged(handle, args);
                        _handleCallbacks.Add(handle, callback);
                        handle.WhenStateChanged += callback;
                    }
                }
                return;
            }

            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised += OnPointerEvent;
            }
        }

        private void UnsubscribeGrabEvents()
        {
            foreach (var pair in _handleCallbacks)
            {
                if (pair.Key != null)
                {
                    pair.Key.WhenStateChanged -= pair.Value;
                }
            }
            _handleCallbacks.Clear();

            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised -= OnPointerEvent;
            }
        }

        private void OnHandleStateChanged(HandGrabInteractable handle, InteractableStateChangeArgs args)
        {
            if (handle == null)
            {
                return;
            }

            if (args.NewState == InteractableState.Select)
            {
                OnHandleSelected(handle);
            }
            else if (args.PreviousState == InteractableState.Select)
            {
                OnHandleUnselected(handle);
            }
        }

        private void OnPointerEvent(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select)
            {
                _isGrabbed = true;
                _hadRequiredHandsThisGrab = true;
                UpdateTargetPreviewVisibility();
                SetPhysicsEnabled(true);
            }
            else if (evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
            {
                _isGrabbed = false;
                TryInstall();
                _hadRequiredHandsThisGrab = false;
                UpdateTargetPreviewVisibility();
            }
        }

        private void OnHandleSelected(HandGrabInteractable handle)
        {
            if (_isInstalled && lockAfterInstalled)
            {
                return;
            }

            if (!_selectedHandles.Contains(handle))
            {
                _selectedHandles.Add(handle);
            }

            _isGrabbed = _selectedHandles.Count > 0;
            if (HasEnoughHandsForInstall())
            {
                _hadRequiredHandsThisGrab = true;
            }
            UpdateTargetPreviewVisibility();
            SetPhysicsEnabled(true);
        }

        private void OnHandleUnselected(HandGrabInteractable handle)
        {
            _selectedHandles.Remove(handle);
            _isGrabbed = _selectedHandles.Count > 0;

            if (!_isGrabbed)
            {
                TryInstall();
                _hadRequiredHandsThisGrab = false;
            }
            UpdateTargetPreviewVisibility();
        }

        private void SetPhysicsEnabled(bool physicsEnabled)
        {
            if (_rigidbody == null)
            {
                return;
            }

            _rigidbody.isKinematic = !physicsEnabled;
            _rigidbody.useGravity = physicsEnabled;

            if (!physicsEnabled)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
        }

        private void SetGrabEnabled(bool enabled)
        {
            if (grabHandles != null)
            {
                for (int i = 0; i < grabHandles.Length; i++)
                {
                    if (grabHandles[i] != null)
                    {
                        grabHandles[i].enabled = enabled;
                    }
                }
            }

            if (grabbable != null)
            {
                grabbable.enabled = enabled;
            }
        }

        private void SetFeedbackColor(Color color)
        {
            if (_pieceFeedbackMaterial != null)
            {
                _pieceFeedbackMaterial.color = color;
            }

            if (_targetPreviewMaterial != null)
            {
                _targetPreviewMaterial.color = color;
            }
        }

        private void UpdateTargetPreviewVisibility()
        {
            if (targetPreviewRenderer == null || _isInstalled)
            {
                return;
            }

            targetPreviewRenderer.enabled = !showTargetPreviewOnlyWhileGrabbed || _isGrabbed;
        }

        private string GetConfiguredActionId()
        {
            if (installedAction != null && !string.IsNullOrWhiteSpace(installedAction.ActionId))
            {
                return installedAction.ActionId.Trim();
            }

            return string.IsNullOrWhiteSpace(actionIdOverride) ? string.Empty : actionIdOverride.Trim();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (installedAction != null && !string.IsNullOrWhiteSpace(installedAction.ActionId))
            {
                actionIdOverride = installedAction.ActionId;
            }
            else if (!string.IsNullOrEmpty(actionIdOverride))
            {
                actionIdOverride = actionIdOverride.Trim();
            }

            if (!string.IsNullOrEmpty(sourceId))
            {
                sourceId = sourceId.Trim();
            }

            if (!string.IsNullOrEmpty(context))
            {
                context = context.Trim();
            }

            positionTolerance = Mathf.Max(0f, positionTolerance);
            angleTolerance = Mathf.Clamp(angleTolerance, 0f, 180f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = HasValidInstallPose() ? Color.green : Color.yellow;

            if (pieceAnchorA != null)
            {
                Gizmos.DrawWireSphere(pieceAnchorA.position, 0.035f);
            }
            if (pieceAnchorB != null)
            {
                Gizmos.DrawWireSphere(pieceAnchorB.position, 0.035f);
            }

            Gizmos.color = Color.cyan;
            if (targetSocketA != null)
            {
                Gizmos.DrawWireSphere(targetSocketA.position, positionTolerance);
            }
            if (targetSocketB != null)
            {
                Gizmos.DrawWireSphere(targetSocketB.position, positionTolerance);
            }
        }
#endif
    }
}
