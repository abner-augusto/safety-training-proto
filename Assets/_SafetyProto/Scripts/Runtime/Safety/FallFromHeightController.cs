using System.Collections;
using Oculus.Interaction.Locomotion;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.PPE;
using UnityEngine;

namespace SafetyProto.Runtime.Safety
{
    /// <summary>
    /// A3 — controls the player's fall-from-height behaviour on the scaffold.
    ///
    /// The Meta <see cref="FirstPersonLocomotor"/> applies gravity whenever the player is not
    /// grounded (e.g. they step off a scaffold edge). That is undesirable while the player is
    /// correctly tied off, and is the deliberate lesson when they try to start work without
    /// tying off.
    ///
    /// Two levers:
    ///  - <b>Suspend</b> the fall while the lanyard is locked to the correct anchor — sets
    ///    <see cref="FirstPersonLocomotor.GravityFactor"/> to 0 so the player walks the deck
    ///    freely but cannot fall (no rope restraint).
    ///  - <b>Trigger</b> a controlled fall for the consequence event — restores gravity, plays
    ///    an <c>OVRScreenFade</c> blackout, recenters the player to a safe spawn, and re-grounds.
    /// </summary>
    public class FallFromHeightController : MonoBehaviour, ISessionResettable
    {
        [Header("References")]
        [Tooltip("Meta FirstPersonLocomotor. Auto-resolved from playerRig if empty.")]
        [SerializeField] private FirstPersonLocomotor locomotor;

        [Tooltip("Lanyard whose lock/retract state suspends or restores the fall.")]
        [SerializeField] private RetractableLanyardController lanyard;

        [Header("Recenter")]
        [Tooltip("OVRCameraRig root. Used to recenter the player after a controlled fall.")]
        [SerializeField] private Transform playerRig;
        [Tooltip("Head transform (CenterEyeAnchor). Auto-resolved from playerRig if empty.")]
        [SerializeField] private Transform playerHead;
        [Tooltip("Where the player lands after a controlled fall (ground level / safe spawn).")]
        [SerializeField] private Transform safeFallSpawn;

        [Header("Fall Timing")]
        [SerializeField] private float fadeOutDuration = 0.8f;
        [Tooltip("Seconds held black while the player is dropped/relocated (covers the motion).")]
        [SerializeField] private float holdBlackDuration = 1.0f;
        [SerializeField] private float fadeInDuration = 0.5f;

        private float _defaultGravityFactor = 1f;
        private bool _fallSuspended;
        private bool _falling;

        /// <summary>True while the fall is suspended (player anchored correctly).</summary>
        public bool IsFallSuspended => _fallSuspended;

        /// <summary>True when the lanyard is locked to the correct anchor — anchored has precedence.</summary>
        public bool IsAnchored => lanyard != null && lanyard.IsLockedCorrectly;

        private void Start()
        {
            if (locomotor == null && playerRig != null)
                locomotor = playerRig.GetComponentInChildren<FirstPersonLocomotor>(true);

            if (locomotor == null)
                SafetyLog.Warning("[FallFromHeightController] FirstPersonLocomotor não encontrado — controle de queda inativo.", this);
            else
                _defaultGravityFactor = locomotor.GravityFactor;

            if (playerHead == null && playerRig != null)
                playerHead = PlayerRecenter.ResolveHead(playerRig);

            if (lanyard != null)
            {
                lanyard.onLanyardLocked.AddListener(OnLanyardLocked);
                lanyard.onLanyardRetracted.AddListener(OnLanyardRetracted);
            }
            else
            {
                SafetyLog.Warning("[FallFromHeightController] lanyard não atribuído — gravidade não será suspensa ao ancorar.", this);
            }
        }

        private void OnDestroy()
        {
            if (lanyard != null)
            {
                lanyard.onLanyardLocked.RemoveListener(OnLanyardLocked);
                lanyard.onLanyardRetracted.RemoveListener(OnLanyardRetracted);
            }
        }

        // ── Lanyard-driven suspend/restore ────────────────────────

        private void OnLanyardLocked(bool isCorrectAnchor)
        {
            // Only a correct anchor protects the player. A wrong anchor must still let them fall.
            if (isCorrectAnchor) SuspendFall();
            else RestoreFall();
        }

        private void OnLanyardRetracted() => RestoreFall();

        /// <summary>Stop the player from falling (anchored) while keeping horizontal walking.</summary>
        public void SuspendFall()
        {
            if (locomotor == null || _fallSuspended) return;
            locomotor.GravityFactor = 0f;
            _fallSuspended = true;
            SafetyLog.Info("[FallFromHeightController] Queda suspensa — jogador ancorado corretamente.", this);
        }

        /// <summary>Restore the player's ability to fall (off-tether).</summary>
        public void RestoreFall()
        {
            if (locomotor == null || !_fallSuspended) return;
            locomotor.GravityFactor = _defaultGravityFactor;
            _fallSuspended = false;
            SafetyLog.Info("[FallFromHeightController] Queda reativada — jogador desconectado.", this);
        }

        // ── Consequence: controlled fall ──────────────────────────

        /// <summary>
        /// Plays the controlled fall consequence: restore gravity, fade to black, relocate the
        /// player to the safe spawn while black, re-ground, fade back in. Skipped when the player
        /// is correctly anchored. Yield this from the gate's consequence coroutine.
        /// </summary>
        public IEnumerator TriggerControlledFall()
        {
            if (IsAnchored)
            {
                SafetyLog.Info("[FallFromHeightController] Queda ignorada — jogador ancorado corretamente.", this);
                yield break;
            }

            if (_falling) yield break;
            _falling = true;

            // Make sure gravity is active for the drop (the off-tether state).
            RestoreFall();

            var fade = OVRScreenFade.instance;
            float prevFadeTime = fade != null ? fade.fadeTime : 0f;

            if (fade != null)
            {
                fade.fadeTime = fadeOutDuration;
                fade.FadeOut();
                yield return new WaitForSeconds(fadeOutDuration);
            }

            // Under black: relocate the player to the safe spawn (represents having fallen and
            // being returned to ground) and re-ground via the locomotor.
            yield return new WaitForSeconds(holdBlackDuration);

            if (playerRig != null && safeFallSpawn != null)
            {
                if (playerHead == null)
                    playerHead = PlayerRecenter.ResolveHead(playerRig);
                PlayerRecenter.Recenter(playerRig, playerHead, safeFallSpawn);
            }

            // EnableMovement re-grounds the character controller at the new position.
            if (locomotor != null) locomotor.EnableMovement();

            if (fade != null)
            {
                fade.fadeTime = fadeInDuration;
                fade.FadeIn();
                yield return new WaitForSeconds(fadeInDuration);
                fade.fadeTime = prevFadeTime;
            }

            _falling = false;
        }

        // ── ISessionResettable ────────────────────────────────────

        public void ResetSession()
        {
            // Never leave the next session with gravity suspended from a prior anchored state.
            if (locomotor != null) locomotor.GravityFactor = _defaultGravityFactor;
            _fallSuspended = false;
            _falling = false;
        }
    }
}
