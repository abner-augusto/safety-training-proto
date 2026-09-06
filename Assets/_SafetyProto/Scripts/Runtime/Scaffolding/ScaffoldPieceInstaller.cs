using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Actions;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Utils;
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
        [SerializeField, ActionId] private string actionId = string.Empty;
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
        [Tooltip("When enabled, pieceAnchorA may align with either targetSocketA or targetSocketB (and pieceAnchorB with the remaining socket). Useful for symmetric pieces that can be installed in either direction.")]
        [SerializeField] private bool allowAnySlotDirection = false;
        [Tooltip("When enabled, the piece can be installed even if rotated 180 degrees around the anchor's Y axis. Useful for symmetric pieces like planks or rails.")]
        [SerializeField] private bool allow180Rotation = false;

        // ── Tolerances ───────────────────────────────────────────

        [Header("Tolerances")]
        [SerializeField] private float positionTolerance = 0.12f;
        [Tooltip("Max yaw error in degrees (about the vertical axis). Kept TIGHT — a piece rotated " +
                 "the wrong way around Y is rejected.")]
        [SerializeField] private float yawTolerance = 15f;
        [Tooltip("Max tilt error in degrees (roll/pitch off-vertical). Kept LOOSE — a slightly-" +
                 "tilted but correctly-oriented piece still validates; ApplySocketPose snaps it " +
                 "perfectly upright on accept.")]
        [SerializeField] private float tiltTolerance = 35f;

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

        // ── Refused install ──────────────────────────────────────

        [Header("Instalação Recusada")]
        [Tooltip("Desfaz a instalação e devolve a peça quando as regras de segurança recusam a " +
                 "tentativa (ex.: talabarte ainda não ancorado, no modo Guiado). A peça só volta " +
                 "depois que o popup de aviso é fechado.")]
        [SerializeField] private bool revertInstallWhenRefused = true;
        [Tooltip("Rede de segurança: se nenhum popup fechar nesse tempo (s) após a recusa, a peça " +
                 "volta assim mesmo, para não travar a tarefa. 0 = esperar apenas pelo popup.")]
        [SerializeField] private float revertFallbackSeconds = 10f;

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

        // ── Audio Feedback ───────────────────────────────────────

        [Header("Audio Feedback")]
        [Tooltip("AudioSource used to play 3D spatialized placement/install sounds. Auto-found or created if null.")]
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Sound played when the piece is successfully installed and locked.")]
        [SerializeField] private AudioClip installSound;
        [Tooltip("Optional hint chime when entering valid install pose.")]
        [SerializeField] private AudioClip validPoseSound;
        [Tooltip("Optional sound when released in an invalid pose.")]
        [SerializeField] private AudioClip invalidReleaseSound;
        [SerializeField, Range(0f, 1f)] private float installVolume = 1.0f;

        // ── Events ───────────────────────────────────────────────

        [Header("Events")]
        public UnityEvent onEnteredValidPose;
        public UnityEvent onExitedValidPose;
        public UnityEvent onInstalled;
        public UnityEvent onInvalidRelease;
        [Tooltip("Disparado quando uma instalação já concluída é desfeita porque a tentativa foi " +
                 "recusada. Use para reverter o que onInstalled tiver acionado.")]
        public UnityEvent onInstallReverted;

        // ── Private state ────────────────────────────────────────

        private Rigidbody  _rigidbody;
        private Collider[] _colliders;
        private Material   _pieceFeedbackMaterial;
        private Material   _targetPreviewMaterial;
        private bool _isGrabbed;
        private bool _isInstalled;
        private bool _wasValidPose;
        private bool _reachedTwoHands;
        private Transform _activeAnchor;
        private Transform _activeSocket;

        private bool _revertPending;
        private float _revertDeadline;
        private System.Action<ActionRefusedEventArgs> _onActionRefused;
        private System.Action<PopupClosedEventArgs> _onPopupClosed;

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

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            WarnIfAudioRunsOnCodeDefaults();

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

            // No bus means no protocol: the piece could still snap into its socket but would
            // never hear a refusal, so it would install and lock with no way back. Disabling is
            // the honest outcome.
            if (!this.IsEventBusReady()) return;

            _onActionRefused ??= OnActionRefused;
            _onPopupClosed ??= OnPopupClosed;
            SafetyProto.Core.EventBus.Instance.Subscribe(_onActionRefused);
            SafetyProto.Core.EventBus.Instance.Subscribe(_onPopupClosed);
        }

        private void OnDisable()
        {
            if (grabbable != null)
                grabbable.WhenPointerEventRaised -= OnPointerEvent;

            var bus = SafetyProto.Core.EventBus.Instance;
            if (bus == null) return;

            if (_onActionRefused != null) bus.Unsubscribe(_onActionRefused);
            if (_onPopupClosed != null) bus.Unsubscribe(_onPopupClosed);
        }

        private void OnDestroy()
        {
            if (_pieceFeedbackMaterial != null) Destroy(_pieceFeedbackMaterial);
            if (_targetPreviewMaterial != null) Destroy(_targetPreviewMaterial);
        }

        private void LateUpdate()
        {
            if (_revertPending && revertFallbackSeconds > 0f && Time.time >= _revertDeadline)
            {
                SafetyLog.Warning($"[ScaffoldPieceInstaller] '{name}' revertido sem confirmação de popup — nenhum aviso foi fechado a tempo.", this);
                RevertInstall();
            }

            if (_isInstalled) return;

            bool valid = HasValidInstallPose();
            if (valid != _wasValidPose)
            {
                _wasValidPose = valid;
                if (valid)
                {
                    onEnteredValidPose?.Invoke();
                    if (validPoseSound != null && _isGrabbed)
                    {
                        EnsureAudioSource();
                        audioSource.PlayOneShot(validPoseSound, 0.4f);
                    }
                }
                else
                {
                    onExitedValidPose?.Invoke();
                }
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
                if (invalidReleaseSound != null)
                {
                    EnsureAudioSource();
                    audioSource.PlayOneShot(invalidReleaseSound, 0.5f);
                }
                return;
            }

            Install();
        }

        /// <summary>
        /// Undoes an install the rule engine refused: the piece leaves the socket, becomes
        /// grabbable again and travels back to its home pose, so the participant can retry it
        /// after satisfying the precondition (anchoring the lanyard). No-op when the piece is
        /// not installed.
        /// </summary>
        public void RevertInstall()
        {
            _revertPending = false;
            if (!_isInstalled) return;

            ResetInstall();

            if (returnHome != null)
            {
                returnHome.enabled = true;
                returnHome.BeginReturnNow();
            }

            onInstallReverted?.Invoke();
            SafetyLog.Info($"[ScaffoldPieceInstaller] '{name}' desinstalado — tentativa recusada pelas regras de segurança.", this);
        }

        public void ResetInstall()
        {
            _revertPending = false;
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

        // ── Refused install ──────────────────────────────────────

        /// <summary>
        /// The rule engine declined the attempt this piece published. The piece is already
        /// snapped and locked at this point, so it has to come back out — but only after the
        /// participant has had the warning in front of them, which is why the revert waits for
        /// the popup to close instead of yanking the piece away mid-sentence.
        /// </summary>
        private void OnActionRefused(ActionRefusedEventArgs args)
        {
            if (!revertInstallWhenRefused || !_isInstalled || _revertPending) return;

            var mine = GetConfiguredActionId();
            if (string.IsNullOrEmpty(mine) ||
                !string.Equals(args.ActionId, mine, System.StringComparison.Ordinal)) return;

            // An empty SourceId means the emitter did not identify itself — accept it rather
            // than leave the piece stuck.
            if (!string.IsNullOrEmpty(args.SourceId) &&
                !string.Equals(args.SourceId, ResolvedSourceId, System.StringComparison.Ordinal)) return;

            _revertPending = true;
            _revertDeadline = Time.time + revertFallbackSeconds;
        }

        private void OnPopupClosed(PopupClosedEventArgs _)
        {
            if (_revertPending) RevertInstall();
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
            if (installSound != null)
            {
                EnsureAudioSource();
                audioSource.PlayOneShot(installSound, installVolume);
            }
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

            // 1. Normal: A -> SA (and B -> SB for TwoSockets)
            if (IsAnchorAligned(pieceAnchorA, targetSocketA) &&
                (installMode == InstallMode.SingleSocket || IsAnchorAligned(pieceAnchorB, targetSocketB)))
            {
                _activeAnchor = pieceAnchorA;
                _activeSocket = targetSocketA;
                return true;
            }

            if (allowAnySlotDirection)
            {
                // 2. Reversed: A -> SB (and B -> SA for TwoSockets)
                if (targetSocketB != null && IsAnchorAligned(pieceAnchorA, targetSocketB) &&
                    (installMode == InstallMode.SingleSocket || IsAnchorAligned(pieceAnchorB, targetSocketA)))
                {
                    _activeAnchor = pieceAnchorA;
                    _activeSocket = targetSocketB;
                    return true;
                }

                // 3. Optional: B -> SA (SingleSocket only, as TwoSockets is covered by Case 2)
                if (installMode == InstallMode.SingleSocket && pieceAnchorB != null && IsAnchorAligned(pieceAnchorB, targetSocketA))
                {
                    _activeAnchor = pieceAnchorB;
                    _activeSocket = targetSocketA;
                    return true;
                }

                // 4. Optional: B -> SB (SingleSocket only, as TwoSockets is covered by Case 1)
                if (installMode == InstallMode.SingleSocket && pieceAnchorB != null && targetSocketB != null && IsAnchorAligned(pieceAnchorB, targetSocketB))
                {
                    _activeAnchor = pieceAnchorB;
                    _activeSocket = targetSocketB;
                    return true;
                }
            }

            return false;
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

            if (IsRotationAligned(pieceAnchor.rotation, socket.rotation)) return true;

            if (allow180Rotation)
            {
                // Check if it matches after a 180 degree rotation around the Y axis
                Quaternion flippedRotation = socket.rotation * Quaternion.Euler(0, 180, 0);
                if (IsRotationAligned(pieceAnchor.rotation, flippedRotation)) return true;
            }

            return false;
        }

        // B11 — per-axis rotation gate. Decomposes the piece→socket delta into yaw (about the
        // vertical axis) and tilt (roll/pitch, i.e. how far the piece's up axis leans off the
        // socket's up axis), and gates each against its own tolerance: yaw tight, tilt loose. So a
        // slightly-leaning-but-correctly-facing piece passes, while a piece spun the wrong way
        // around Y is still rejected.
        private bool IsRotationAligned(Quaternion pieceRot, Quaternion socketRot)
        {
            // Delta expressed in the socket's local frame.
            Quaternion delta = Quaternion.Inverse(socketRot) * pieceRot;

            // Tilt: angle between the (local) up axes.
            float tilt = Vector3.Angle(Vector3.up, delta * Vector3.up);
            if (tilt > tiltTolerance) return false;

            // Yaw: rotation of the forward axis about the vertical, measured on the horizontal plane.
            Vector3 flatFwd = Vector3.ProjectOnPlane(delta * Vector3.forward, Vector3.up);
            float yaw = flatFwd.sqrMagnitude < 1e-6f ? 0f : Vector3.Angle(Vector3.forward, flatFwd);
            return yaw <= yawTolerance;
        }

        private void ApplySocketPose()
        {
            if (_activeAnchor == null || _activeSocket == null) return;

            // Determine if we need to apply the 180 degree flip: pick whichever orientation the
            // piece is actually closest to (robust to the loose tilt tolerance).
            Quaternion targetRotation = _activeSocket.rotation;
            if (allow180Rotation)
            {
                Quaternion flipped = _activeSocket.rotation * Quaternion.Euler(0, 180, 0);
                if (Quaternion.Angle(_activeAnchor.rotation, flipped) <
                    Quaternion.Angle(_activeAnchor.rotation, _activeSocket.rotation))
                {
                    targetRotation = flipped;
                }
            }

            Vector3    anchorLocalPos = transform.InverseTransformPoint(_activeAnchor.position);
            Quaternion anchorLocalRot = Quaternion.Inverse(transform.rotation) * _activeAnchor.rotation;

            Quaternion targetRootRot = targetRotation * Quaternion.Inverse(anchorLocalRot);
            Vector3    targetRootPos = _activeSocket.position - targetRootRot * anchorLocalPos;

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

            var resolvedContext = string.IsNullOrWhiteSpace(context) ? null : context.Trim();

            ActionEvents.PublishActionAttempt(actionId, ResolvedSourceId, resolvedContext, transform.position, interactorId);
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

        /// <summary>
        /// A clip with no AudioSource anywhere on the object means EnsureAudioSource will build
        /// one at play time from the hard-coded values below — so the spatial settings an author
        /// sees in the scene are not the ones this piece will use. Say so at load, while there
        /// is still something to fix.
        /// </summary>
        private void WarnIfAudioRunsOnCodeDefaults()
        {
            if (audioSource != null) return;
            if (installSound == null && validPoseSound == null && invalidReleaseSound == null) return;

            SafetyLog.Warning(
                $"[ScaffoldPieceInstaller] '{name}' tem clipe de áudio mas nenhum AudioSource. " +
                "Um source será criado em runtime com valores padrão do código (spatialBlend 1, " +
                "min 0.5 m, max 10 m), ignorando qualquer ajuste feito na cena.", this);
        }

        private void EnsureAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.spatialBlend = 1f;
                    audioSource.spatialize = true;
                    audioSource.playOnAwake = false;
                    audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                    audioSource.minDistance = 0.5f;
                    audioSource.maxDistance = 10f;
                }
            }
        }

        private string GetConfiguredActionId()
        {
            return string.IsNullOrWhiteSpace(actionId) ? string.Empty : actionId.Trim();
        }

        /// <summary>Identity this piece stamps on the attempts it publishes, so a refusal can be
        /// matched back to it even when several pieces share one action id.</summary>
        private string ResolvedSourceId =>
            string.IsNullOrWhiteSpace(sourceId) ? gameObject.name : sourceId.Trim();

        // ── Editor ───────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(actionId)) actionId = actionId.Trim();

            if (!string.IsNullOrEmpty(sourceId)) sourceId = sourceId.Trim();
            if (!string.IsNullOrEmpty(context))  context  = context.Trim();

            positionTolerance = Mathf.Max(0f, positionTolerance);
            yawTolerance      = Mathf.Clamp(yawTolerance, 0f, 180f);
            tiltTolerance     = Mathf.Clamp(tiltTolerance, 0f, 180f);
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
