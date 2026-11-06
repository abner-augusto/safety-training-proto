using System;
using System.Collections;
using Oculus.Interaction.Locomotion;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Analytics;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>How the busy sequence should handle the player locomotor while the screen is black.</summary>
    public enum LocomotorMode
    {
        /// <summary>Leave the locomotor untouched.</summary>
        None,
        /// <summary>Disable before the teleport; restore its previous enabled state afterwards
        /// (phase-transition style — pairs with a ground probe).</summary>
        ToggleEnabled,
        /// <summary>Call FirstPersonLocomotor.EnableMovement() after the teleport to re-ground
        /// the character controller.</summary>
        EnableMovement,
    }

    /// <summary>
    /// Options describing one teleport flavor. Lets the phase transition
    /// (PhaseController.ExecutePhaseTransition) and the dashboard's recenter command keep their
    /// behavioral differences while sharing the single fade-out -> recenter -> reground ->
    /// fade-in sequence and the single busy guard. The controlled fall never recenters
    /// (see FallFromHeightController.TriggerControlledFall).
    /// </summary>
    public struct RecenterOptions
    {
        public float FadeOutDuration;
        public float HoldBlackDuration;
        public float FadeInDuration;

        /// <summary>Suspend DashboardGate.PoseBroadcastSuspended for the sequence (phase: true, fall: false).</summary>
        public bool SuspendPoseBroadcast;

        /// <summary>Probe the ground under the anchor before re-enabling the locomotor (phase: true, fall: false).</summary>
        public bool UseGroundProbe;

        /// <summary>Restore OVRScreenFade.fadeTime to its pre-sequence value afterwards (fall: true).</summary>
        public bool RestoreFadeTime;

        /// <summary>Ground-ready predicate. Only consulted when UseGroundProbe is true.</summary>
        public Func<bool> GroundReady;

        /// <summary>Safety-net timeout for the ground probe (ignored when UseGroundProbe is false).</summary>
        public float GroundWaitTimeout;

        /// <summary>Runs right after the teleport, while the screen is black (object show/hide,
        /// transition panel, blackout message — the service itself stays UI-agnostic).</summary>
        public Action OnBlackout;

        /// <summary>Runs right before the fade-in starts (e.g. hide the transition panel).</summary>
        public Action OnBeforeFadeIn;

        public LocomotorMode LocomotorHandling;
    }

    /// <summary>
    /// Owns the single fade -> recenter -> reground coroutine and the single busy guard shared by
    /// every teleport trigger (phase transition, evaluator dashboard recenter).
    /// The callers differ deliberately (ground probe, DashboardGate suspension, locomotor
    /// handling, fade-time restore); RecenterOptions is how each keeps its own shape.
    /// Also owns the <see cref="RoomCenterCalibration"/> every placement uses, so the physical
    /// room center, not wherever the participant stands, maps onto each phase's anchor.
    /// </summary>
    public class RecenterService : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private Transform playerRig;
        [Tooltip("Head transform (CenterEyeAnchor). Auto-resolved from playerRig if empty.")]
        [SerializeField] private Transform playerHead;
        [Tooltip("Player locomotor (FirstPersonLocomotor). Auto-resolved from playerRig if empty.")]
        [SerializeField] private Behaviour playerLocomotor;

        private bool _isBusy;
        private Behaviour _activeLocomotor;
        private bool _activeLocomotorWasEnabled;
        private bool _activeSuspendedPoseBroadcast;
        private readonly RoomCenterCalibration _roomCenter = new RoomCenterCalibration();
        private OVRDisplay _subscribedDisplay;

        /// <summary>True for the whole sequence, including both fades — an overlapping request
        /// during fade-in is still refused.</summary>
        public bool IsBusy => _isBusy;

        /// <summary>Who recorded the room center that recenters currently use, if anyone.</summary>
        public RoomCenterSource RoomCenterSource => _roomCenter.Source;

        private void Awake()
        {
            if (playerLocomotor == null && playerRig != null)
                playerLocomotor = ResolveLocomotor(playerRig);

            if (playerHead == null && playerRig != null)
                playerHead = PlayerRecenter.ResolveHead(playerRig);
        }

        private void Start()
        {
            // OVRManager creates its display in its own Awake, so look it up only after every Awake.
            _subscribedDisplay = OVRManager.display;
            if (_subscribedDisplay != null)
                _subscribedDisplay.RecenteredPose += OnSystemRecentered;
        }

        private void OnDestroy()
        {
            if (_subscribedDisplay != null)
                _subscribedDisplay.RecenteredPose -= OnSystemRecentered;

            // A reset reloads the scene mid-sequence and kills the caller's coroutine before the
            // gate is lifted; the gate is process-wide, so it would mute the pose stream for every
            // later session.
            CancelActive();
        }

        private void OnSystemRecentered()
        {
            if (!_roomCenter.HasValue) return;
            _roomCenter.Clear();
            SafetyLog.Warning("[RecenterService] Recentralização do sistema (botão Meta) — calibração do centro da sala descartada. Calibre novamente com o participante no X.", this);
            EventBus.Instance?.Publish(new PlayspaceNoticeEventArgs { Kind = PlayspaceNoticeKind.RoomCenterDiscarded });
        }

        /// <summary>
        /// Records where the participant's head stands now as the physical room center and the
        /// way they face as the room's forward. An automatic session-start capture never
        /// replaces an operator calibration.
        /// </summary>
        public bool CaptureRoomCenter(RoomCenterSource source)
        {
            if (source == RoomCenterSource.SessionStart && _roomCenter.Source == RoomCenterSource.Operator)
                return false;

            if (playerHead == null && playerRig != null)
                playerHead = PlayerRecenter.ResolveHead(playerRig);
            if (playerRig == null || playerHead == null)
            {
                SafetyLog.Warning("[RecenterService] Calibração do centro da sala ignorada — rig ou cabeça não atribuídos.", this);
                return false;
            }

            _roomCenter.Capture(playerRig, playerHead.position, playerHead.rotation, source);
            return true;
        }

        /// <summary>
        /// Moves the rig at once so the calibrated room center lands on <paramref name="anchor"/>
        /// facing its yaw; without a calibration, the live head lands there instead. A
        /// <see cref="RecenterAnchorArea"/> on the anchor clamps where the body may arrive.
        /// </summary>
        public void PlaceOn(Transform anchor)
        {
            if (playerRig == null || anchor == null) return;

            if (playerHead == null)
                playerHead = PlayerRecenter.ResolveHead(playerRig);

            if (_roomCenter.TryGetWorld(playerRig, out var center, out var centerYaw))
            {
                var area = anchor.GetComponent<RecenterAnchorArea>();
                Vector3 reference = area != null && playerHead != null
                    ? RoomCenterCalibration.LandingReference(center, centerYaw,
                        RoomCenterCalibration.BodyAxis(playerHead.position, playerHead.rotation), area.ClampLanding)
                    : center;
                PlayerRecenter.Recenter(playerRig, reference, centerYaw, anchor.position, anchor.eulerAngles.y);
            }
            else if (playerHead != null)
            {
                PlayerRecenter.Recenter(playerRig, playerHead, anchor);
            }
            else
            {
                playerRig.position = anchor.position;
                playerRig.rotation = Quaternion.Euler(0f, anchor.rotation.eulerAngles.y, 0f);
                Physics.SyncTransforms();
            }
        }

        private static Behaviour ResolveLocomotor(Transform root)
        {
            foreach (var b in root.GetComponentsInChildren<Behaviour>(true))
                if (b != null && b.GetType().Name == "FirstPersonLocomotor")
                    return b;
            return null;
        }

        /// <summary>
        /// Runs the fade-out -> recenter -> reground -> fade-in sequence described by
        /// <paramref name="options"/>. No-op (with a SafetyLog.Warning) when already busy or
        /// when <paramref name="anchor"/> is null.
        /// </summary>
        public IEnumerator RecenterTo(Transform anchor, RecenterOptions options)
        {
            if (_isBusy)
            {
                SafetyLog.Warning("[RecenterService] RecenterTo ignorado — outra transição já em andamento.", this);
                yield break;
            }
            if (anchor == null)
            {
                SafetyLog.Warning("[RecenterService] RecenterTo ignorado — âncora nula.", this);
                yield break;
            }

            _isBusy = true;

            if (playerHead == null && playerRig != null)
                playerHead = PlayerRecenter.ResolveHead(playerRig);
            if (playerLocomotor == null && playerRig != null)
                playerLocomotor = ResolveLocomotor(playerRig);

            var fade = OVRScreenFade.instance;
            float prevFadeTime = fade != null ? fade.fadeTime : 0f;

            if (fade != null)
            {
                fade.fadeTime = options.FadeOutDuration;
                fade.FadeOut();
                yield return new WaitForSeconds(options.FadeOutDuration);
            }

            _activeLocomotor = options.LocomotorHandling == LocomotorMode.ToggleEnabled ? playerLocomotor : null;
            _activeLocomotorWasEnabled = _activeLocomotor != null && _activeLocomotor.enabled;
            if (_activeLocomotor != null) _activeLocomotor.enabled = false;

            _activeSuspendedPoseBroadcast = options.SuspendPoseBroadcast;
            if (_activeSuspendedPoseBroadcast)
                DashboardGate.PoseBroadcastSuspended = true;

            PlaceOn(anchor);

            options.OnBlackout?.Invoke();

            if (options.UseGroundProbe)
            {
                float elapsed = 0f;
                bool groundReady = false;
                while (elapsed < options.HoldBlackDuration || (!groundReady && elapsed < options.GroundWaitTimeout))
                {
                    if (!groundReady) groundReady = options.GroundReady == null || options.GroundReady.Invoke();
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (!groundReady)
                    SafetyLog.Warning($"[RecenterService] Chão não confirmado em {options.GroundWaitTimeout}s — religando locomotor mesmo assim.", this);
            }
            else
            {
                yield return new WaitForSeconds(options.HoldBlackDuration);
            }

            if (options.LocomotorHandling == LocomotorMode.ToggleEnabled)
            {
                if (_activeLocomotor != null) _activeLocomotor.enabled = _activeLocomotorWasEnabled;
            }
            else if (options.LocomotorHandling == LocomotorMode.EnableMovement)
            {
                (playerLocomotor as FirstPersonLocomotor)?.EnableMovement();
            }
            _activeLocomotor = null;

            if (_activeSuspendedPoseBroadcast)
                DashboardGate.PoseBroadcastSuspended = false;
            _activeSuspendedPoseBroadcast = false;

            options.OnBeforeFadeIn?.Invoke();

            if (fade != null)
            {
                fade.fadeTime = options.FadeInDuration;
                fade.FadeIn();
                yield return new WaitForSeconds(options.FadeInDuration);
                if (options.RestoreFadeTime)
                    fade.fadeTime = prevFadeTime;
            }

            _isBusy = false;
        }

        /// <summary>
        /// Cancels an in-flight sequence and unwinds locomotor/gate/flags. Does NOT stop the
        /// caller's coroutine — RecenterTo executes as a nested enumerator inside whichever
        /// MonoBehaviour started it (PhaseController, RecenterCommandHandler, ...), so the
        /// caller must StopAllCoroutines() on itself first. This only resets this service's own
        /// state so a stopped-mid-flight sequence doesn't leave the locomotor disabled or the
        /// dashboard gate suspended forever. Used by PhaseController.CancelSimulationTransition
        /// via the SessionSimulator.
        /// </summary>
        public void CancelActive()
        {
            if (!_isBusy) return;
            if (_activeLocomotor != null)
                _activeLocomotor.enabled = _activeLocomotorWasEnabled;
            if (_activeSuspendedPoseBroadcast)
                DashboardGate.PoseBroadcastSuspended = false;
            _activeLocomotor = null;
            _activeSuspendedPoseBroadcast = false;
            _isBusy = false;
        }
    }
}
