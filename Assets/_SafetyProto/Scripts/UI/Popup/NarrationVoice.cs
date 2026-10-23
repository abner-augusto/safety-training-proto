using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Head-locked voice-over channel shared by every narrated surface (onboarding, DICAs, popups,
    /// info boards, final report). Playing a new line cuts the previous one, so a participant who
    /// confirms a step early never hears two lines overlap. <see cref="PlayAfterCurrent"/> instead
    /// holds one pending line until the current one ends, for surfaces that must not cut a DICA.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class NarrationVoice : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;

        private AudioClip _pending;
        private bool _currentInterruptible;

        /// <summary>True while a line is playing or waiting to play.</summary>
        public bool IsBusy => _pending != null || (audioSource != null && audioSource.isPlaying);

        /// <summary>True when nothing plays, or the line playing was started as interruptible
        /// (an info board), so an urgent line may cut it instead of waiting.</summary>
        public bool CanCutCurrent => audioSource == null || !audioSource.isPlaying || _currentInterruptible;

        /// <summary>Seconds left on the line playing now; zero when idle.</summary>
        public float RemainingSeconds =>
            audioSource != null && audioSource.isPlaying && audioSource.clip != null
                ? Mathf.Max(0f, audioSource.clip.length - audioSource.time)
                : 0f;

        private void Awake()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
        }

        private void Update()
        {
            if (_pending == null || audioSource.isPlaying) return;
            var next = _pending;
            _pending = null;
            _currentInterruptible = false;
            audioSource.clip = next;
            audioSource.Play();
        }

        /// <summary>Play <paramref name="clip"/>, replacing whatever line is playing or pending. Null just stops.</summary>
        public void Play(AudioClip clip) => PlayLine(clip, interruptible: false);

        /// <summary>Like <see cref="Play"/>, but the line yields to urgent lines (see <see cref="CanCutCurrent"/>).
        /// For optional reading aids such as the info boards.</summary>
        public void PlayInterruptible(AudioClip clip) => PlayLine(clip, interruptible: true);

        private void PlayLine(AudioClip clip, bool interruptible)
        {
            _pending = null;
            audioSource.Stop();
            if (clip == null) return;
            _currentInterruptible = interruptible;
            audioSource.clip = clip;
            audioSource.Play();
        }

        /// <summary>Play <paramref name="clip"/> as soon as the current line ends (right away when
        /// idle or when the current line is interruptible). Replaces a line already pending.</summary>
        public void PlayAfterCurrent(AudioClip clip)
        {
            if (clip == null) return;
            if (CanCutCurrent)
            {
                Play(clip);
                return;
            }
            _pending = clip;
        }

        /// <summary>True when <paramref name="clip"/> is the line playing now.</summary>
        public bool IsPlaying(AudioClip clip)
            => clip != null && audioSource != null && audioSource.isPlaying && audioSource.clip == clip;

        /// <summary>Stop <paramref name="clip"/> only if it is the line playing or pending, leaving
        /// any other surface's line alone.</summary>
        public void StopIfCurrent(AudioClip clip)
        {
            if (clip == null || audioSource == null) return;
            if (_pending == clip) _pending = null;
            if (audioSource.isPlaying && audioSource.clip == clip) audioSource.Stop();
        }

        public void Stop()
        {
            _pending = null;
            if (audioSource != null) audioSource.Stop();
        }
    }
}
