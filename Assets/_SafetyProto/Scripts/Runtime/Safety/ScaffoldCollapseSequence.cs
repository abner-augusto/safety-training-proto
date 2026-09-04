using System;
using System.Collections;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Runtime.Safety
{
    /// <summary>
    /// Plays the scaffold-collapse consequence and nothing else: creak, safety-mesh tear, tip-over,
    /// the player losing the deck, a scripted fall, and the fade that lands before impact. It knows
    /// nothing about sessions, score, popups, or the finish screen — <see cref="Play"/> returns with
    /// the player mid-fall under a black screen and the caller decides what happens next.
    ///
    /// The rig is driven by explicit transform writes rather than SetParent: PlayerRecenter already
    /// writes playerRig.position/rotation directly, reparenting the OVR rig would fight OVRCameraRig
    /// and the locomotion stack, and only explicit writes let the inherited rotation be clamped at
    /// MaxFollowTiltDegrees while translation still follows the pivot in full.
    ///
    /// See plans/027-scaffold-collapse-design.md for the beat sheet and the timing rationale.
    /// </summary>
    public class ScaffoldCollapseSequence : MonoBehaviour, ISessionResettable
    {
        public const string BeatLocked = "locked";
        public const string BeatCreak = "creak";
        public const string BeatTear = "tear";
        public const string BeatTilt = "tilt";
        public const string BeatDetach = "detach";
        public const string BeatFade = "fade";
        public const string BeatBlackout = "blackout";

        /// <summary>
        /// Raised as each beat begins, carrying one of the Beat* constants. Editor tooling (the
        /// session simulator) subscribes to record ordering; nothing in the runtime depends on it.
        /// Intentionally a plain static event rather than an entry in Core/Events — no runtime
        /// system needs this channel, and the simulator is Editor-only.
        /// </summary>
        public static event Action<string> OnBeat;

        [Header("References")]
        [Tooltip("Empty at the front edge of the scaffold base, parent of the 'andaime' hierarchy. Rotating it tips the structure and all its colliders together.")]
        [SerializeField] private Transform collapsePivot;
        [Tooltip("Local axis of collapsePivot the structure tips around. X tips forward for the default Blender import orientation; flip the sign to tip the other way.")]
        [SerializeField] private Vector3 tiltAxis = Vector3.right;
        [Tooltip("The 'tela_proteção' GameObject that tears away. Optional.")]
        [SerializeField] private GameObject safetyMesh;
        [Tooltip("OVRCameraRig root. The rig rides the pivot, then falls.")]
        [SerializeField] private Transform playerRig;
        [Tooltip("FirstPersonLocomotor (or any Behaviour to disable for the duration). Auto-resolved from playerRig if empty.")]
        [SerializeField] private Behaviour locomotor;

        [Header("Timing (seconds)")]
        [SerializeField] private float creakDuration = 1.2f;
        [SerializeField] private float tearDuration = 0.8f;
        [SerializeField] private float tiltDuration = 3.0f;
        [Tooltip("Fade-out length. It starts when the tilt finishes, so black lands this many seconds later — before the fall would reach the ground.")]
        [SerializeField] private float fadeOutDuration = 0.8f;

        [Header("Motion")]
        [SerializeField] private float tiltAngleDegrees = 55f;
        [Tooltip("0..1 over the tilt phase. Ease-in reads as the structure yielding slowly, then giving way.")]
        [SerializeField] private AnimationCurve tiltCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private float detachAngleDegrees = 30f;
        [Tooltip("Comfort ceiling on how far the camera tilts with the structure. Past this the rig follows translation only.")]
        [SerializeField] private float maxFollowTiltDegrees = 22f;
        [SerializeField] private float tremorAmplitudeDegrees = 0.5f;
        [SerializeField] private float fallGravity = 9.81f;
        [SerializeField] private float maxFallSpeed = 12f;
        [Tooltip("Extra forward pitch applied to the rig across the scripted fall, in degrees.")]
        [SerializeField] private float fallPitchDegrees = 12f;

        [Header("Audio Sources")]
        [Tooltip("3D source positioned on the scaffold. Carries the structural sounds.")]
        [SerializeField] private AudioSource scaffoldAudioSource;
        [Tooltip("2D source on the player rig. Carries what the player hears on their own body.")]
        [SerializeField] private AudioSource playerAudioSource;

        [Header("SFX — Scaffold")]
        [Tooltip("Loops through the creak beat, fades out when the tip-over starts.")]
        [SerializeField] private AudioClip creakLoopSfx;
        [SerializeField, Range(0f, 1f)] private float creakLoopVolume = 0.8f;
        [Tooltip("Safety mesh tearing.")]
        [SerializeField] private AudioClip meshTearSfx;
        [SerializeField, Range(0f, 1f)] private float meshTearVolume = 1f;
        [Tooltip("The structure giving way, at the start of the tip-over.")]
        [SerializeField] private AudioClip structureCollapseSfx;
        [SerializeField, Range(0f, 1f)] private float structureCollapseVolume = 1f;
        [Tooltip("Snap at the moment the player loses the deck.")]
        [SerializeField] private AudioClip boltSnapSfx;
        [SerializeField, Range(0f, 1f)] private float boltSnapVolume = 1f;

        [Header("SFX — Player")]
        [Tooltip("Loops through the scripted fall.")]
        [SerializeField] private AudioClip windRushSfx;
        [SerializeField, Range(0f, 1f)] private float windRushVolume = 0.7f;
        [Tooltip("Muffled impact, played under full black.")]
        [SerializeField] private AudioClip impactSfx;
        [SerializeField, Range(0f, 1f)] private float impactVolume = 1f;

        // Captured on Play() so ResetSession can unwind from any beat, including a mid-sequence
        // StopAllCoroutines from the simulator's cancel path.
        private Quaternion _pivotBaseRotation;
        private Vector3 _rigStartPosition;
        private Quaternion _rigStartRotation;
        private bool _locomotorWasEnabled;
        private bool _stateCaptured;

        private ScaffoldCollapseConfig Config => new ScaffoldCollapseConfig
        {
            TiltAngleDegrees = tiltAngleDegrees,
            DetachAngleDegrees = detachAngleDegrees,
            MaxFollowTiltDegrees = maxFollowTiltDegrees,
            TremorAmplitudeDegrees = tremorAmplitudeDegrees,
            FallGravity = fallGravity,
            MaxFallSpeed = maxFallSpeed,
        };

        private void Awake()
        {
            if (locomotor == null && playerRig != null)
            {
                foreach (var b in playerRig.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b != null && b.GetType().Name == "FirstPersonLocomotor") { locomotor = b; break; }
                }
            }
        }

        private static void RaiseBeat(string beat) => OnBeat?.Invoke(beat);

        /// <summary>
        /// Plays the whole collapse. Returns with the player mid-fall and the screen fully black —
        /// the caller owns everything after that (popup, session end, finish screen). No-op with a
        /// warning when the pivot is missing, so an unwired scene degrades instead of throwing.
        /// </summary>
        public IEnumerator Play()
        {
            if (collapsePivot == null)
            {
                SafetyLog.Warning("[ScaffoldCollapseSequence] collapsePivot não atribuído — colapso do andaime ignorado.", this);
                yield break;
            }

            CaptureState();

            // ── Beat: locked ──────────────────────────────────────
            RaiseBeat(BeatLocked);
            if (locomotor != null) locomotor.enabled = false;

            // ── Beat: creak ───────────────────────────────────────
            RaiseBeat(BeatCreak);
            StartLoop(scaffoldAudioSource, creakLoopSfx, creakLoopVolume);

            var cfg = Config;
            float elapsed = 0f;
            while (elapsed < creakDuration)
            {
                float tremor = ScaffoldCollapseSolver.TremorOffsetDegrees(elapsed, creakDuration, cfg);
                collapsePivot.localRotation = _pivotBaseRotation * Quaternion.AngleAxis(tremor, tiltAxis);
                elapsed += Time.deltaTime;
                yield return null;
            }

            // ── Beat: tear ────────────────────────────────────────
            RaiseBeat(BeatTear);
            PlayOneShot(scaffoldAudioSource, meshTearSfx, meshTearVolume);
            // Cut, not fade. The mesh material does not respond to an alpha ramp, so the tear
            // audio carries the moment and the mesh simply stops existing under it.
            if (safetyMesh != null) safetyMesh.SetActive(false);
            if (tearDuration > 0f)
                yield return new WaitForSeconds(tearDuration);

            // ── Beat: tilt (and, inside it, detach / fall / fade) ──
            RaiseBeat(BeatTilt);
            StopLoop(scaffoldAudioSource);
            PlayOneShot(scaffoldAudioSource, structureCollapseSfx, structureCollapseVolume);

            // One loop drives the tip-over, the ride, the detach, the scripted fall and the fade,
            // because they overlap: the fade begins while the player is still falling so black
            // arrives before any ground contact could jolt the view.
            Vector3 rigPivotLocalPosition = collapsePivot.InverseTransformPoint(_rigStartPosition);
            Vector3 worldTiltAxis = collapsePivot.TransformDirection(tiltAxis).normalized;

            bool detached = false;
            bool fadeStarted = false;
            float fallSpeed = 0f;
            float fallElapsed = 0f;
            float fallWindow = 0f;
            float endTime = tiltDuration + fadeOutDuration;

            elapsed = 0f;
            while (elapsed < endTime)
            {
                float dt = Time.deltaTime;
                float tiltT = tiltDuration > 0f ? Mathf.Clamp01(elapsed / tiltDuration) : 1f;
                float angle = ScaffoldCollapseSolver.TiltAngle(tiltCurve.Evaluate(tiltT), cfg);
                collapsePivot.localRotation = _pivotBaseRotation * Quaternion.AngleAxis(angle, tiltAxis);

                if (!detached && ScaffoldCollapseSolver.ShouldDetach(angle, cfg))
                {
                    detached = true;
                    // How much of the sequence is left to fall through. The pitch is scaled against
                    // this rather than against endTime, so it actually reaches fallPitchDegrees by
                    // the blackout no matter where the detach angle lands on the curve.
                    fallWindow = Mathf.Max(0.01f, endTime - elapsed);
                    RaiseBeat(BeatDetach);
                    PlayOneShot(scaffoldAudioSource, boltSnapSfx, boltSnapVolume);
                    StartLoop(playerAudioSource, windRushSfx, windRushVolume);
                }

                if (playerRig != null)
                {
                    if (!detached)
                    {
                        // Ride the deck: full translation, rotation clamped for comfort.
                        float followTilt = ScaffoldCollapseSolver.FollowTiltDegrees(angle, cfg);
                        playerRig.position = collapsePivot.TransformPoint(rigPivotLocalPosition);
                        playerRig.rotation = Quaternion.AngleAxis(followTilt, worldTiltAxis) * _rigStartRotation;
                    }
                    else
                    {
                        fallElapsed += dt;
                        fallSpeed = ScaffoldCollapseSolver.IntegrateFallSpeed(fallSpeed, dt, cfg);
                        playerRig.position += Vector3.down * (fallSpeed * dt);

                        float pitchT = Mathf.Clamp01(fallElapsed / fallWindow);
                        float followTilt = ScaffoldCollapseSolver.FollowTiltDegrees(angle, cfg);
                        playerRig.rotation =
                            Quaternion.AngleAxis(followTilt + fallPitchDegrees * pitchT, worldTiltAxis) * _rigStartRotation;
                    }
                }

                if (!fadeStarted && elapsed >= tiltDuration)
                {
                    fadeStarted = true;
                    RaiseBeat(BeatFade);
                    StartFadeOut();
                }

                elapsed += dt;
                yield return null;
            }

            // ── Beat: blackout ────────────────────────────────────
            RaiseBeat(BeatBlackout);
            StopLoop(playerAudioSource);
            PlayOneShot(playerAudioSource, impactSfx, impactVolume);

            // Level the rig, keeping only its yaw. The ride and the fall leave it pitched by
            // maxFollowTiltDegrees + fallPitchDegrees, and nothing else would ever undo that — the
            // consequence is terminal, so the participant would read the whole finish screen with
            // their horizon tilted. The screen is already fully black here, so the correction is
            // invisible: discomfort is the lesson during the fall, not while reading the report.
            if (playerRig != null)
            {
                playerRig.rotation = Quaternion.Euler(0f, playerRig.eulerAngles.y, 0f);
                Physics.SyncTransforms();
            }

            SafetyLog.Info("[ScaffoldCollapseSequence] Colapso do andaime concluído — tela em preto.", this);
        }

        private void CaptureState()
        {
            if (_stateCaptured) return;

            _pivotBaseRotation = collapsePivot.localRotation;

            if (playerRig != null)
            {
                _rigStartPosition = playerRig.position;
                _rigStartRotation = playerRig.rotation;
            }

            _locomotorWasEnabled = locomotor != null && locomotor.enabled;
            _stateCaptured = true;
        }

        private void StartFadeOut()
        {
            if (OVRScreenFade.instance == null)
            {
                SafetyLog.Warning("[ScaffoldCollapseSequence] OVRScreenFade ausente — a queda terminará sem escurecer a tela.", this);
                return;
            }
            OVRScreenFade.instance.fadeTime = fadeOutDuration;
            OVRScreenFade.instance.FadeOut();
        }

        private static void PlayOneShot(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null || clip == null) return;
            source.PlayOneShot(clip, volume);
        }

        private static void StartLoop(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null || clip == null) return;
            source.clip = clip;
            source.volume = volume;
            source.loop = true;
            source.Play();
        }

        private static void StopLoop(AudioSource source)
        {
            if (source == null || !source.isPlaying) return;
            source.Stop();
            source.loop = false;
            source.clip = null;
        }

        // ── ISessionResettable ────────────────────────────────────

        /// <summary>
        /// Returns the scaffold, the mesh, the rig and the locomotor to their pre-collapse state.
        /// Must survive being called from any beat: the simulator's cancel path calls
        /// StopAllCoroutines mid-sequence, and the rehearsal button calls this to replay.
        /// Restarting a session normally reloads the scene, so this is a safety net, not the
        /// primary reset path.
        /// </summary>
        public void ResetSession()
        {
            StopAllCoroutines();

            if (collapsePivot != null && _stateCaptured)
                collapsePivot.localRotation = _pivotBaseRotation;

            if (safetyMesh != null)
                safetyMesh.SetActive(true);

            if (playerRig != null && _stateCaptured)
            {
                playerRig.SetPositionAndRotation(_rigStartPosition, _rigStartRotation);
                Physics.SyncTransforms();
            }

            if (locomotor != null && _stateCaptured)
                locomotor.enabled = _locomotorWasEnabled;

            StopLoop(scaffoldAudioSource);
            StopLoop(playerAudioSource);

            _stateCaptured = false;
        }
    }
}
