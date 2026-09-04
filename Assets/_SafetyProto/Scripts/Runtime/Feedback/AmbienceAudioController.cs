using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>
    /// Manages the dynamic ambient soundscape for the VR workplace safety training simulation.
    ///
    /// Elevation-aware dual-layer soundscape:
    ///   - Ground Layer (2D): Construction site background activity bed (trucks, distant tools).
    ///     Attenuates slightly as the player ascends the scaffold structure.
    ///   - Height Layer (2D): Exterior wind gusts whistling through scaffold tubing.
    ///     Fades in smoothly based on the player head Y elevation, reinforcing NR-35 height awareness.
    ///   - Optional 3D Anchor: Localized spatial emitter (e.g. diesel generator / compressor).
    ///     Provides a fixed acoustic anchor for head-tracked spatial orientation in VR.
    /// </summary>
    public class AmbienceAudioController : MonoBehaviour, ISessionResettable
    {
        [Header("Player Tracking")]
        [Tooltip("Transform representing the player's head (HMD / Camera). Auto-resolved to Camera.main if null.")]
        [SerializeField] private Transform playerHead;

        [Header("Audio Sources")]
        [Tooltip("2D source for ground construction site ambience. Auto-created if left empty.")]
        [SerializeField] private AudioSource groundAudioSource;

        [Tooltip("2D source for scaffold elevation wind ambience. Auto-created if left empty.")]
        [SerializeField] private AudioSource heightAudioSource;

        [Tooltip("Optional 3D source for ground equipment (e.g. generator).")]
        [SerializeField] private AudioSource generatorAudioSource;

        [Header("Audio Clips")]
        [SerializeField] private AudioClip groundAmbienceClip;
        [SerializeField] private AudioClip heightAmbienceClip;
        [SerializeField] private AudioClip generatorClip;

        [Header("Volume Configuration")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;

        [Tooltip("Target volume of the ground ambience at ground level.")]
        [SerializeField, Range(0f, 1f)] private float groundMaxVolume = 0.25f;

        [Tooltip("Minimum volume of the ground ambience when at maximum scaffold height.")]
        [SerializeField, Range(0f, 1f)] private float groundMinVolume = 0.08f;

        [Tooltip("Target volume of the height wind ambience at maximum scaffold height.")]
        [SerializeField, Range(0f, 1f)] private float heightMaxVolume = 0.35f;

        [Tooltip("Minimum volume of the height wind ambience at ground level.")]
        [SerializeField, Range(0f, 1f)] private float heightMinVolume = 0.0f;

        [Tooltip("Volume for the 3D generator audio source.")]
        [SerializeField, Range(0f, 1f)] private float generatorVolume = 0.4f;

        [Header("Elevation Mapping (Meters)")]
        [Tooltip("Y position considered ground level (full ground bed, no wind).")]
        [SerializeField] private float groundElevationY = 0.0f;

        [Tooltip("Y position considered scaffold deck height (full wind bed, attenuated ground).")]
        [SerializeField] private float scaffoldDeckElevationY = 3.5f;

        [Tooltip("Speed of volume crossfade interpolation per second.")]
        [SerializeField, Range(0.5f, 10f)] private float crossfadeSpeed = 2.5f;

        [Header("Fade-In")]
        [Tooltip("Duration in seconds to smoothly fade in ambience on start.")]
        [SerializeField, Range(0f, 5f)] private float fadeInDuration = 1.5f;

        // Current smoothed volume states
        private float _currentGroundVolume;
        private float _currentHeightVolume;
        private float _fadeInMultiplier = 0f;
        private bool _isPaused;

        private void Awake()
        {
            EnsureAudioSources();
        }

        private void Start()
        {
            if (playerHead == null && Camera.main != null)
            {
                playerHead = Camera.main.transform;
            }

            StartAmbiencePlayback();
        }

        private void OnEnable()
        {
            if (this.IsEventBusReady())
            {
                EventBus.Instance.onSessionPaused.AddListener(OnSessionPaused);
                EventBus.Instance.onSessionResumed.AddListener(OnSessionResumed);
            }
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onSessionPaused.RemoveListener(OnSessionPaused);
                EventBus.Instance.onSessionResumed.RemoveListener(OnSessionResumed);
            }
        }

        private void Update()
        {
            if (_isPaused) return;

            // Handle smooth startup fade-in
            if (_fadeInMultiplier < 1f)
            {
                _fadeInMultiplier = fadeInDuration > 0f
                    ? Mathf.MoveTowards(_fadeInMultiplier, 1f, Time.deltaTime / fadeInDuration)
                    : 1f;
            }

            UpdateElevationBlend();
        }

        private void EnsureAudioSources()
        {
            if (groundAudioSource == null)
            {
                groundAudioSource = Create2DAudioSource("GroundAmbience_Source", groundAmbienceClip);
            }

            if (heightAudioSource == null)
            {
                heightAudioSource = Create2DAudioSource("HeightAmbience_Source", heightAmbienceClip);
            }

            if (generatorAudioSource != null && generatorClip != null)
            {
                generatorAudioSource.clip = generatorClip;
                generatorAudioSource.loop = true;
                generatorAudioSource.spatialBlend = 1f;
                generatorAudioSource.spatialize = true;
            }
        }

        private AudioSource Create2DAudioSource(string objectName, AudioClip clip)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var src = child.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f; // 2D Stereo
            src.spatialize = false;
            src.volume = 0f;
            return src;
        }

        private void StartAmbiencePlayback()
        {
            if (groundAudioSource != null && groundAudioSource.clip != null && !groundAudioSource.isPlaying)
            {
                groundAudioSource.Play();
            }

            if (heightAudioSource != null && heightAudioSource.clip != null && !heightAudioSource.isPlaying)
            {
                heightAudioSource.Play();
            }

            if (generatorAudioSource != null && generatorAudioSource.clip != null && !generatorAudioSource.isPlaying)
            {
                generatorAudioSource.volume = generatorVolume * masterVolume;
                generatorAudioSource.Play();
            }

            SafetyLog.Info("[AmbienceAudioController] Ambient soundscape playback started.", this);
        }

        private void UpdateElevationBlend()
        {
            float playerY = playerHead != null ? playerHead.position.y : groundElevationY;

            // Calculate elevation factor (0.0 at ground, 1.0 at scaffold deck or higher)
            float elevationRange = Mathf.Max(0.1f, scaffoldDeckElevationY - groundElevationY);
            float elevationFactor = Mathf.Clamp01((playerY - groundElevationY) / elevationRange);

            // Calculate target volumes based on elevation
            float targetGroundVol = Mathf.Lerp(groundMaxVolume, groundMinVolume, elevationFactor);
            float targetHeightVol = Mathf.Lerp(heightMinVolume, heightMaxVolume, elevationFactor);

            // Smooth interpolation
            float step = crossfadeSpeed * Time.deltaTime;
            _currentGroundVolume = Mathf.MoveTowards(_currentGroundVolume, targetGroundVol, step);
            _currentHeightVolume = Mathf.MoveTowards(_currentHeightVolume, targetHeightVol, step);

            // Apply volumes with fade-in and master multiplier
            float effectiveMaster = masterVolume * _fadeInMultiplier;

            if (groundAudioSource != null)
            {
                groundAudioSource.volume = _currentGroundVolume * effectiveMaster;
            }

            if (heightAudioSource != null)
            {
                heightAudioSource.volume = _currentHeightVolume * effectiveMaster;
            }
        }

        private void OnSessionPaused(SessionPausedEventArgs _)
        {
            _isPaused = true;
            groundAudioSource?.Pause();
            heightAudioSource?.Pause();
            generatorAudioSource?.Pause();
        }

        private void OnSessionResumed(SessionResumedEventArgs _)
        {
            _isPaused = false;
            groundAudioSource?.UnPause();
            heightAudioSource?.UnPause();
            generatorAudioSource?.UnPause();
        }

        public void ResetSession()
        {
            _fadeInMultiplier = 0f;
            _currentGroundVolume = 0f;
            _currentHeightVolume = 0f;
            _isPaused = false;

            if (groundAudioSource != null)
            {
                groundAudioSource.Stop();
                groundAudioSource.volume = 0f;
            }

            if (heightAudioSource != null)
            {
                heightAudioSource.Stop();
                heightAudioSource.volume = 0f;
            }

            generatorAudioSource?.Stop();
            StartAmbiencePlayback();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (scaffoldDeckElevationY <= groundElevationY)
            {
                scaffoldDeckElevationY = groundElevationY + 1f;
            }
        }
#endif
    }
}
