using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Head-locked voice-over channel shared by the pre-session popups. Playing a new line cuts the
    /// previous one, so a participant who confirms a step early never hears two lines overlap.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class NarrationVoice : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;

        private void Awake()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
        }

        /// <summary>Play <paramref name="clip"/>, replacing whatever line is playing. Null just stops.</summary>
        public void Play(AudioClip clip)
        {
            audioSource.Stop();
            if (clip == null) return;
            audioSource.clip = clip;
            audioSource.Play();
        }

        public void Stop()
        {
            if (audioSource != null) audioSource.Stop();
        }
    }
}
