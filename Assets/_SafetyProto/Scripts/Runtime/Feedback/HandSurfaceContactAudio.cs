using SafetyProto.Runtime.PPE;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>Plays a spatial cue when a hand begins touching a stationary surface.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HandSurfaceLimiter))]
    public sealed class HandSurfaceContactAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip contactClip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.3f;
        [Tooltip("Seconds without contact before another touch can play a sound.")]
        [SerializeField, Min(0.01f)] private float releaseDelay = 0.12f;
        [SerializeField, Min(0f)] private float cooldown = 0.2f;

        private HandSurfaceLimiter _limiter;
        private AudioSource _source;
        private float _lastContactTime = float.NegativeInfinity;
        private float _nextSoundTime;

        private void Awake()
        {
            _limiter = GetComponent<HandSurfaceLimiter>();
            var emitter = new GameObject("Hand surface contact audio");
            emitter.transform.SetParent(transform, false);
            _source = emitter.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.dopplerLevel = 0f;
            _source.minDistance = 0.3f;
            _source.maxDistance = 5f;
            _source.rolloffMode = AudioRolloffMode.Linear;
        }

        private void OnEnable() => _limiter.WhenSurfaceContact += OnContact;

        private void OnDisable()
        {
            _limiter.WhenSurfaceContact -= OnContact;
            _source.Stop();
        }

        private void OnDestroy()
        {
            if (_source != null) Destroy(_source.gameObject);
        }

        private void OnContact(Vector3 point)
        {
            if (!TryBeginContact(Time.unscaledTime) || contactClip == null) return;
            _source.transform.position = point;
            _source.PlayOneShot(contactClip, volume);
        }

        private bool TryBeginContact(float now)
        {
            bool entered = now - _lastContactTime >= releaseDelay;
            _lastContactTime = now;
            if (!entered || now < _nextSoundTime) return false;
            _nextSoundTime = now + cooldown;
            return true;
        }
    }
}
